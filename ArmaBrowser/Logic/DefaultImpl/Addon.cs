﻿using System;
using System.Collections.Generic;
using System.Linq;
using ArmaBrowser.Data;

namespace ArmaBrowser.Logic
{
    class Addon : ObjectNotify, IAddon
    {

        private bool _isActive;
        private long _activationOrder;
        private bool _canActived;
        private IEnumerable<Uri> _downlandUris;
        private IEnumerable<AddonKey> _keyNames;
        private bool? _isEasyInstallable;
        private bool _isInstalled;
        private int _progressValue;


        public string Name { get; internal set; }

        public string ModName { get; internal set; }

        public string DisplayText { get; internal set; }

        public string Version { get; internal set; }

        public string Path { get; set; }

        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (value == _isActive) return;
                _isActive = IsInstalled && value;

                _activationOrder = IsActive ? DateTime.Now.Ticks : 0;
                OnPropertyChanged("ActivationOrder");
                OnPropertyChanged();
            }
        }

        public bool CanActived
        {
            get { return ((IsEasyInstallable.HasValue && IsEasyInstallable.Value) || IsInstalled) && _canActived; }
            set
            {
                if (_canActived == value) return;
                _canActived = value;
                OnPropertyChanged();
            }
        }

        public long ActivationOrder
        {
            get
            {
                return _activationOrder;
            }
        }

        public IEnumerable<AddonKey> KeyNames
        {
            get { return _keyNames ?? (_keyNames = new AddonKey[0]); }
            internal set { _keyNames = value; }
        }

        public bool IsInstalled
        {
            get { return _isInstalled; }
            internal set
            {
                if (value == _isInstalled) return;
                _isInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged("IsEasyInstallable");
                OnPropertyChanged("CanActived");
                OnPropertyChanged("CanSharing");
            }
        }

        public IEnumerable<Uri> DownlandUris
        {
            get { return _downlandUris ?? (_downlandUris = new Uri[0]); }
            internal set
            {
                if (Equals(value, _downlandUris)) return;
                _downlandUris = value;
                OnPropertyChanged("IsInstallable");
                OnPropertyChanged();

            }
        }

        public bool IsInstallable
        {
            get { return _downlandUris != null && _downlandUris.Any(); }
        }

        public int ProgressValue
        {
            get { return _progressValue; }
            set
            {
                if (value == _progressValue) return;
                _progressValue = value;
                OnPropertyChanged();
            }
        }

        public bool IsArmaDefaultPath { get; set; }

        public string CommandlinePath
        {
            get
            {
                return IsArmaDefaultPath ? Name : Path;
            }
        }

        public bool? IsEasyInstallable
        {
            get { return _isEasyInstallable.HasValue ? (bool?)(_isEasyInstallable.Value && !_isInstalled) : null; }
            set
            {
                if (value == _isEasyInstallable) return;
                _isEasyInstallable = value;
                OnPropertyChanged();
                OnPropertyChanged("CanSharing");
                OnPropertyChanged("CanActived");
            }
        }


        public bool CanSharing
        {
            get { return IsInstalled && _isEasyInstallable.HasValue && !_isEasyInstallable.Value; }
        }
    }
}
