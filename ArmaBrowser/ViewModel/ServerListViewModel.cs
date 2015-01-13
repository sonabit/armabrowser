﻿using ArmaBrowser.Logic;
using ArmaBrowser.Logic.DefaultImpl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace ArmaBrowser.ViewModel
{
    class ServerListViewModel : LogicModelBase
    {
        #region Fields

        private LogicContext _context;
        private ListCollectionView _serverItemsView;
        private string _textFilter = string.Empty;
        private System.Net.IPEndPoint _ipEndPointFilter = null;
        private IServerItem _selectedServerItem;
        private readonly ObservableCollection<IAddon> _useAddons = new ObservableCollection<IAddon>();
        private IAddon _selectedAddon;
        private uint _loadingBusy;
        private string _selectedEndPoint;
        private bool _runAsAdmin;
        private bool _launchWithoutHost;
        private IEnumerable<System.Net.IPEndPoint> _lastItems = new System.Net.IPEndPoint[0];

        private System.Threading.CancellationTokenSource _reloadingCts = new System.Threading.CancellationTokenSource();
        private readonly ObservableCollection<LogEntry> _actionLog = new ObservableCollection<LogEntry>();
        private bool _isJoining;


        #endregion Fields


        #region Properties

        public Collection<LogEntry> ActionLog
        {
            get { return _actionLog; }
        }

        public Collection<IServerItem> ServerItems
        {
            get { return _context.ServerItems; }
        }

        public Collection<IAddon> Addons
        {
            get { return _context.Addons; }
        }

        public Collection<IAddon> UseAddons
        {
            get { return _useAddons; }
        }

        public ListCollectionView ServerItemsView
        {
            get { return _serverItemsView ?? CreateServerItemsView(); }
        }

        private ListCollectionView CreateServerItemsView()
        {
            _serverItemsView = new ListCollectionView(ServerItems) { Filter = OnServerItemsFilter };

            // Sorting
            _serverItemsView.SortDescriptions.Add(new System.ComponentModel.SortDescription { PropertyName = "GroupName", Direction = System.ComponentModel.ListSortDirection.Ascending });
            _serverItemsView.SortDescriptions.Add(new System.ComponentModel.SortDescription { PropertyName = "CurrentPlayerCount", Direction = System.ComponentModel.ListSortDirection.Descending });

            // Grouping           
            _serverItemsView.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));

            return _serverItemsView;
        }

        public string TextFilter
        {
            get { return _textFilter; }
            set
            {
                if (_textFilter == value) return;
                _textFilter = value;

                _ipEndPointFilter = null;

                if (!string.IsNullOrEmpty(_textFilter))
                {
                    _textFilter = _textFilter.Trim();
                    var doppelpunktpos = _textFilter.IndexOf(':');
                    if (doppelpunktpos > 6 && (_textFilter.Length > doppelpunktpos + 4))
                    {
                        var ipstr = _textFilter.Substring(0, doppelpunktpos);
                        var portstr = _textFilter.Substring(doppelpunktpos + 1, _textFilter.Length - doppelpunktpos - 1);
                        System.Net.IPAddress ip;
                        int port;
                        if (System.Net.IPAddress.TryParse(ipstr, out ip)
                                && Int32.TryParse(portstr, out port)
                                && port > System.Net.IPEndPoint.MinPort
                                && port < System.Net.IPEndPoint.MaxPort)
                        {
                            _ipEndPointFilter = new System.Net.IPEndPoint(ip, port);
                        }
                    }
                }

                OnPropertyChanged();

                Properties.Settings.Default.TextFilter = _textFilter;
                ServerItemsView.Refresh();
                RememberLastVisiblyItems();
            }
        }

        private void RememberLastVisiblyItems()
        {
            _lastItems = ServerItemsView.Cast<IServerItem>().Take(50).Select(i => { return new System.Net.IPEndPoint(i.Host, i.QueryPort); }).ToArray();

        }

        public IAddon SelectedAddon
        {
            get { return _selectedAddon; }
            set
            {
                if (_selectedAddon == value) return;
                _selectedAddon = value;
                OnPropertyChanged();
                //ServerItemsView.Refresh();
            }
        }

        public IServerItem SelectedServerItem
        {
            get { return _selectedServerItem; }
            set
            {
                if (_selectedServerItem == value) return;
                _selectedServerItem = value;
                _selectedEndPoint = _selectedServerItem == null ? string.Empty : string.Format("{0}:{1}", _selectedServerItem.Host, _selectedServerItem.Port);
                _context.RefreshServerInfoAsync(items: new[] { _selectedServerItem })
                    .ContinueWith(t => RefreshUseAddons());
                RefreshUseAddons();
                OnPropertyChanged();

            }
        }

        public bool RunAsAdmin
        {
            get { return _runAsAdmin; }
            set
            {
                _runAsAdmin = value;
                OnPropertyChanged();
            }
        }

        #endregion Properties

        public ServerListViewModel()
        {
            //if (Properties.Settings.Default.Hosts == null)
            //    Properties.Settings.Default.Hosts = new Properties.HostConfigCollection();


            UiTask.Initialize();
            _context = new LogicContext();
            _context.LiveAction += _context_LiveAction;

            TextFilter = Properties.Settings.Default.TextFilter;
            _selectedEndPoint = Properties.Settings.Default.LastPlayedHost;

            LookForInstallation();

            var recentlyHosts = Properties.Settings.Default.RecentlyHosts.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Reverse().ToArray();
            var serverItems = _context.AddServerItems(recentlyHosts);
            foreach (var item in serverItems)
            {
                item.LastPlayed = DateTime.Now;
            }
            _context.RefreshServerInfoAsync(serverItems)
                .ContinueWith(t =>
                    {
                        ServerItemsView.Refresh();
                    }, UiTask.TaskScheduler);

            Task.Run((Action)EndlessRefreshSelecteItem);
        }

        void _context_LiveAction(object sender, string e)
        {
            UiTask.Run((list, item) =>
            {
                var entry = new LogEntry { Text = item, Time = DateTime.Now };
                list.Insert(0, entry);
                //return entry;
            }, _actionLog, e)
                //.ContinueWith(t =>
                //    {
                //        Task.Delay(5000);
                //        return t.Result;
                //    }
                //    )
                // .ContinueWith(t => {
                //     if (_actionLog.Count > 0)
                //         _actionLog.RemoveAt(0);
                // },UiTask.TaskScheduler)
             ;
        }

        public void Reload()
        {
            BeginLoading();

            _reloadingCts.Cancel();
            var oldsrc = _reloadingCts;
            _reloadingCts = new System.Threading.CancellationTokenSource();
            CancellationToken token = _reloadingCts.Token;
            //oldsrc.Dispose();

            var lastSelected = _selectedEndPoint;

            Task.Factory.StartNew(o => ReloadInternal((CancellationToken)o), token, token)
                .ContinueWith(t =>
                {

                    //if (!string.IsNullOrEmpty(lastSelected))
                    //{
                    //    SelectedServerItem = ServerItems.FirstOrDefault(s => string.Format("{0}:{1}", s.Host, s.Port) == lastSelected);
                    //}
                    if (t.Status == TaskStatus.RanToCompletion)
                        RememberLastVisiblyItems();
                    EndLoading();

                })
                .ContinueWith(t => _actionLog.Clear(), UiTask.TaskScheduler);

        }

        void Test()
        {
            var lastItems = ServerItemsView.Cast<IServerItem>().Select(i => { return new System.Net.IPEndPoint(i.Host, i.Port); });

        }

        public bool LoadingBusy
        {
            get { return _loadingBusy > 0; }
        }

        private void BeginLoading()
        {
            _loadingBusy++;
            OnPropertyChanged("LoadingBusy");
        }

        private void EndLoading()
        {
            _loadingBusy--;
            if (_loadingBusy < 0) _loadingBusy = 0;
            OnPropertyChanged("LoadingBusy");
        }

        void ReloadInternal(CancellationToken cancellationToken)
        {
            if (_ipEndPointFilter != null)
                _context.ReloadServerItem(_ipEndPointFilter, cancellationToken);
            else
                _context.ReloadServerItems(_lastItems, cancellationToken);
            RefreshUseAddons();
        }

        private bool OnServerItemsFilter(object o)
        {
            var result = true;
            var item = (IServerItem)o;

            if (_ipEndPointFilter != null)
            {
                result = item.Host.Equals(_ipEndPointFilter.Address) && item.Port == _ipEndPointFilter.Port;
                return result;
            }

            if (!string.IsNullOrWhiteSpace(_textFilter))
            {
                var strs = _textFilter.Trim().Split(' ').ToArray();

                foreach (var s in strs)
                {
                    if (s == string.Empty) continue;

                    if (!result)
                        break;

                    if (s[0] == '-')
                    {
                        var s2 = s.Substring(1);
                        result = result && !(item.FullText.IndexOf(s2, StringComparison.CurrentCultureIgnoreCase) > -1);
                        continue;
                    }
                    if (s[0] == '>')
                    {
                        var s2 = s.Substring(1);
                        var playerCount = 0;
                        if (Int32.TryParse(s2, out playerCount))
                        {
                            result = result && item.CurrentPlayerCount > playerCount;
                            continue;
                        }
                    }
                    result = result && (item.FullText.IndexOf(s, StringComparison.CurrentCultureIgnoreCase) > -1);

                }

            }
            //if (result && _selectedAddon != null)
            //{
            //    result = item.Mods.Contains(_selectedAddon.ModName);
            //}

            return result;

        }

        private void RefreshUseAddons()
        {
            var t = UiTask.Run(_useAddons.Clear);
            if (_selectedServerItem == null || _selectedServerItem.Mods == null)
            {
                return;
            }
            var sortNr = 1;
            var hostAddons = _selectedServerItem.Mods.Select(m => new { SortNr = sortNr++, ModName = m });

            var endpoint = string.Format("{0}:{1}", _selectedServerItem.Host, _selectedServerItem.Port);
            var hostCfgItem = HostConfigCollection.Default.Cast<HostConfig>().FirstOrDefault(h => h.EndPoint == endpoint);

            if (hostCfgItem != null)
            {
                sortNr = 1;
                hostAddons = hostCfgItem.PossibleAddons.Split(';').Select(m => new { SortNr = sortNr++, ModName = m }).ToArray();

            }

            var mods = (from mod in Addons
                        join selectedMod in hostAddons on mod.ModName equals selectedMod.ModName into selectedMods
                        from selectedMod in selectedMods.DefaultIfEmpty()
                        let Sortnr = selectedMod == null ? 0 : selectedMod.SortNr
                        let selectedModName = selectedMod == null ? null : selectedMod.ModName
                        orderby Sortnr
                        select new { mod, selectedMod = selectedModName, Sortnr }).ToArray();

            var s = _selectedServerItem.Signatures ?? string.Empty;
            var hostAddonKeys = s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToArray();

            // Addons automatisch ab- und auswählen
            foreach (var item in mods)
            {
                item.mod.IsActive = !string.IsNullOrWhiteSpace(item.selectedMod);
                Thread.Sleep(1);
                item.mod.CanActived = false;
                if (hostAddonKeys.Any())
                {
                    foreach (var addonKey in item.mod.KeyNames)
                    {
                        if (hostAddonKeys.Contains(addonKey))
                        {
                            item.mod.CanActived = true;
                            break;
                        }
                    }
                }

            }

            // Ausgewählt Addons in die Liste für die Anzeige hinzufügen
            UiTask.Run((list, selectedMods) =>
            {
                list.Clear();
                foreach (var mod in selectedMods.Where(m => m.mod.IsActive).OrderBy(m => m.Sortnr))
                    list.Add(mod.mod);
            }, _useAddons, mods);

        }

        public string ArmaPath
        {
            get
            {
                return _context.ArmaPath;
            }
        }

        public string ArmaVersion
        {
            get
            {
                return _context.ArmaVersion;
            }
        }

        public bool IsJoinig
        {
            get { return _isJoining; }
            set
            {
                _isJoining = value;
                OnPropertyChanged();
            }
        }


        public bool LaunchWithoutHost
        {
            get { return _launchWithoutHost; }
            set
            {
                _launchWithoutHost = value;
                OnPropertyChanged();
            }
        }

        internal void OpenArma()
        {
            var host = _selectedServerItem;
            if (_launchWithoutHost)
                host = null;

            if (host != null && host.Port <= System.Net.IPEndPoint.MinPort)
                return;

            var endpoint = host != null ? string.Format("{0}:{1}", host.Host, host.Port) : string.Empty;
            var usedAddons = Addons.Where(a => a.IsActive).OrderBy(a => a.ActivationOrder).ToArray();
            var hostCfgItem = HostConfigCollection.Default.Cast<HostConfig>().FirstOrDefault(h => h.EndPoint == endpoint);
            if (hostCfgItem == null)
            {
                hostCfgItem = new HostConfig
                {
                    EndPoint = endpoint,
                    PossibleAddons = string.Empty
                };
                HostConfigCollection.Default.Add(hostCfgItem);
            }
            hostCfgItem.PossibleAddons = string.Join(";", usedAddons.OrderBy(a => a.ActivationOrder).Select(a => a.ModName).ToArray());



            Properties.Settings.Default.LastPlayedHost = endpoint;
            SaveHistory();

            _context.Open(host, usedAddons.ToArray());
        }

        private void SaveHistory()
        {
            _selectedServerItem.LastPlayed = DateTime.Now;

            // limit addresses to 10 entries
            {
                var items = _context.ServerItems.Where(srv => srv.LastPlayed.HasValue).OrderByDescending(srv => srv.LastPlayed).Skip(10);
                foreach (var item in items)
                {
                    item.LastPlayed = null;
                }
            }

            // Save histroy addresses
            {
                var items = _context.ServerItems.Where(srv => srv.LastPlayed.HasValue).OrderByDescending(srv => srv.LastPlayed).Select(srv => string.Format("{0}:{1}", srv.Host, srv.QueryPort)).ToArray();
                Properties.Settings.Default.RecentlyHosts = string.Join(" ", items);
            }


            // Save favorits
            {
                var items = _context.ServerItems.Where(srv => srv.IsFavorite).Select(srv => string.Format("{0}:{1}", srv.Host, srv.QueryPort)).ToArray();
                Properties.Settings.Default.Favorits = string.Join(" ", items);
            }

            Properties.Settings.Default.Save();
        }

        //internal void TestJson()
        //{
        //    _context.TestJson();
        //}

        private async void EndlessRefreshSelecteItem()
        {
            while (true)
            {
                var mainwin = UiTask.Run(() => App.Current.MainWindow);
                mainwin.Wait();
                if (mainwin == null) break;
                try
                {
                    var task = UiTask.Run(() => App.Current.MainWindow.WindowState != WindowState.Minimized);
                    task.Wait();
                    if (task.IsCompleted && task.Result && !_isJoining)
                    {
                        var item = SelectedServerItem;
                        if (item != null)
                        {
                            _context.RefreshServerInfo(new[] { item });
                        }
                    }
                    await Task.Delay(3000);
                }
                catch
                {

                }
            }
        }

        internal void LookForInstallation()
        {
            _context.LookForArmaPath();
            OnPropertyChanged("ArmaPath");
            OnPropertyChanged("ArmaVersion");
            Properties.Settings.Default.ArmaPath = _context.ArmaPath;
        }

        internal void SaveFavorits()
        {

        }
    }

    class LogEntry
    {
        public string Text { get; set; }
        public DateTime Time { get; set; }
    }
}