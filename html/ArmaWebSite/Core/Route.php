<?php
// Core/Route.php

namespace Core;

/**
 * Handle the URL
 */
class Route{

    private $_uri = array();
    private $_methode = array();
    private static $_route = null;
    
    public static $UriParameter = array();
    
    /**
     * Summary of add
     * @param mixed $uri 
     */
    public function add($uri, $methode = null)
    {
        $this->_uri[] =  $uri ;
        
        if ($methode != null){
            $this->_methode[] = $methode;
        }
    }
    
    public static function AddRoute($uri, $methode = null)
    {
        self::GetRoute()->add($uri, $methode);
    }
    
    
    public static function GetRoute()  {
        if (self::$_route == null)  self::$_route = new Route();
        return self::$_route;
    }
    
    
    /**
     * Makes the thing run!
     */
    public function submit(){
        
        $uriGetParam = isset($_GET['uri']) ? '/' . rtrim($_GET['uri'],'/') : '/Home/Index';
        
        self::$UriParameter = array();
        
        if (!preg_match('#^(\/[a-zA-Z0-9]+)\/?([a-zA-Z0-9\-][a-zA-Z0-9\-]*)?[\/\?]?(.*)#', $uriGetParam, self::$UriParameter))
        {
             header("HTTP/1.0 400 Bad request");
             return;
        }
        
        $controllerName = '';
        foreach ($this->_uri as $key => $value)
        {
            if (strcasecmp($value, self::$UriParameter[1]) == 0){
                $controllerName = $this->_methode[$key];
                $controllerKey = $key;
                break;
            }
        }
        
        
        if ( $controllerName != '') 
        {
            $controllerClassName = $controllerName . 'Controller';
            $controller = new  $controllerClassName();

            $controller->SetControllerName($controllerName);

            $m = isset(self::$UriParameter[2]) && self::$UriParameter[2] != '' ? self::$UriParameter[2] : 'Index';
            
            if (method_exists($controller, $m) && is_callable(array($controller, $m)))
            {
                if (!($controller instanceof Controller)) { throw new \Exception("Invalid operation, expect class Controller"); }

                $controller->ViewPath = 'Views'. DIRECTORY_SEPARATOR . $controllerName .  DIRECTORY_SEPARATOR . $m . '.php';
                $controller->$m();
                //call_user_func(array($controller, $m));
            }
            
            //$methodeExists = method_exists($controller,$m);
            //if ($methodeExists)
            //    $controller->$m();
            else
            {
                header("HTTP/1.0 404 Not Found");
            }
            
        }
        else
        {
            header("HTTP/1.0 404 Not Found");
        }
    }
}


?>