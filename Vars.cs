/*
	by: Rafael Tonello (tonello.rafinha@gmail.com)
	
	Version; 2.0.0.0

	History:
		1.0.0.0 -> 24/01/2018-> First version
		1.1.0.0 -> 24/01/2018-> Prefix for variables
        2.0.0.0 -> 24/01/2018-> Support for http requests (GET, POST and DELETE)
        2.0.1.0 -> 24/01/2018-> Added semaphore resource to access files
        2.0.2.0 -> 09/02/2018-> Added isAvailable function and remove .lock entries from getChilds result
        2.0.2.1 -> 09/02/2018-> Fixed a bug that throwing an exception when the .lock entries were removed from the getChilds result

*/

using Libs;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FilesVars
{
    class Vars
    {
        public class VarValue
        {
            string _value;

            public string AsString
            {
                get { return _value; }
                set { _value = value; }
            }

            public int AsInt
            {
                get
                {
                    int ret = 0;
                    if (!int.TryParse(_value, out ret))
                        ret = 0;

                    return ret;
                }
                set { _value = value.ToString(); }
            }

            public double AsDouble
            {
                get
                {
                    double ret = 0.0;
                    if (!double.TryParse(_value, out ret))
                        ret = 0.0;

                    return ret;
                }
                set { _value = value.ToString(); }
            }

            public bool AsBoolean
            {
                get
                {
                    if (_value.ToLower() == "true")
                        return true;
                    else
                        return false;
                }
                set { _value = value.ToString(); }
            }

            public DateTime AsDateTime
            {
                get
                {
                    DateTime ret;
                    if (DateTime.TryParse(_value, out ret))
                        return ret;
                    else
                        return new DateTime(0, 1, 1, 0, 0, 0);
                    //return DateTime.Parse("01/01/0001 00:00:00");
                }
                set
                {
                    _value = value.ToString();
                }
            }

        }


        private class FileVar
        {
            public string name;
            public string value;
            public bool writed = false;
        }
        private Dictionary<string, FileVar> cache = new Dictionary<string, FileVar>();
        private string appPath = "";
        private string invalidValue = "---////invalid////-----";
        EasyThread th = null;
        bool _useCacheInRam = true;
        string directory = "";
        string varsPrefix;

        HttpUtils httpUtils = new HttpUtils();

        public Vars(string directory = "", bool useCacheInRam = true, string varsPrefix = "")
        {
            this._useCacheInRam = useCacheInRam;
            this.appPath = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + "\\";
            this.varsPrefix = varsPrefix;
            if (directory == "")
                directory = this.appPath + "\\vars";

            if ((directory[directory.Length-1] != '\\') && (directory[directory.Length - 1] != '/'))
                directory += "\\";

            if (directory.IndexOf("http://") == 0)
                directory = directory.Replace("\\", "/");
            else
            {
                if (!System.IO.Directory.Exists(directory))
                    System.IO.Directory.CreateDirectory(directory);
            }

            this.directory = directory;

        }

        private string StringToFileName(string text)
        {
            StringBuilder ret = new StringBuilder();
            foreach (char att in text)
            {
                if ("abcdefghijklmnopqrstuvxywzABCDEFGHIJKLMNOPRSTUVXWYZ0123456789_.-".IndexOf(att) > -1)
                    ret.Append(att);
            }

            return ret.ToString();
        }

        private string getCacheVar(string name)
        {
            //verifica se a variável existe
            string value = this.get(name + ".value", "").AsString;
            if (value != "")
            {
                try
                {
                    //verifica se se ainda é valida
                    DateTime def = new DateTime(1, 1, 1, 0, 0, 0);
                    DateTime expires = this.get(name + ".expires", def).AsDateTime;

                    if (DateTime.Now.CompareTo(expires) < 0)
                        return value;
                    else
                        return "";
                }
                catch { return ""; }



            }
            else
                return "";
        }

        private void setCacheVar(string name, string value, TimeSpan validity)
        {
            this.set(name + ".value", value);
            if (value != "")
            {
                try
                {
                    DateTime expires = DateTime.Now.Add(validity);
                    this.set(name + ".expires", expires);
                }
                catch { }
            }
        }

        private void write(string fName, string value)
        {
            if (fName.ToLower().IndexOf("http://") == 0)
            {
                int tries = 3;
                while (tries > 0)
                {
                    try
                    {
                        httpUtils.httpRequest(fName, value, null, "POST");
                        break;
                    }
                    catch { }
                    tries--;
                }

            }
            else
            {
                waitOne(fName);
                int tries = 3;
                while (tries > 0)
                {
                    try
                    {
                        System.IO.File.WriteAllText(fName, value);
                        tries = 0;
                    }
                    catch { }
                    tries--;
                    Thread.Sleep(10);
                }
                release(fName);
            }
            
        }

        private string read(string fName)
        {
            if (fName.ToLower().IndexOf("http://") == 0)
            {
                int tries = 3;
                while (tries > 0)
                {
                    try
                    {
                        //the server can return invalidValue
                        return httpUtils.httpRequest(fName);
                    }
                    catch { }
                    tries--;
                }
            }
            else
            {
                int tries = 3;
                while (tries > 0)
                {
                    try
                    {
                        waitOne(fName);
                        if (System.IO.File.Exists(fName))
                        {
                            release(fName);
                            return System.IO.File.ReadAllText(fName);
                        }
                        else
                        {
                            release(fName);
                            tries = 0;
                        }
                    }
                    catch { }
                    tries--;
                }
            }
            
            return this.invalidValue;
        }



        private void writeToFile(EasyThread sender, object parameters)
        {
            //operações com arquivos devem ser, preferencialmente, realizadas em threads

            sender.sleep(10);
            int cont = 0;

            while (cont < this.cache.Count)
            {
                try
                {

                    if (!this.cache.ElementAt(cont).Value.writed)
                    {
                        int tries = 5;
                        int currentRetryInterval = 50;
                        while (tries > 0)
                        {
                            try
                            {
                                string name = this.StringToFileName(this.cache.ElementAt(cont).Key);
                                write(directory + name, this.cache.ElementAt(cont).Value.value);
                                this.cache[name].writed = true;
                                tries = 0;
                            }
                            catch
                            {
                                Thread.Sleep(currentRetryInterval);
                                currentRetryInterval += 50;
                            }
                            tries--;
                        }
                    }
                }
                catch
                { }

                cont++;
            }
        }

        public VarValue get(string name, object def)
        {
            name = varsPrefix + name;
            string ret = this.invalidValue;
            if (_useCacheInRam)
            {
                if (cache.ContainsKey(name))
                    ret = cache[name].value;
            }
            if (ret == this.invalidValue)
            {
                try
                {
                    string fName = directory + this.StringToFileName(name);
                    ret = read(fName);
                    if ((!this.cache.ContainsKey(name)) || (this.cache[name] == null)) this.cache[name] = new FileVar { name = name };
                    this.cache[name].value = ret;
                    this.cache[name].writed = true;
                }
                catch
                {
                    ret = invalidValue;
                }
            }


            if (ret != this.invalidValue)
                return new VarValue { AsString = ret };
            else
                return new VarValue { AsString = def is string ? (string)def : def.ToString() };
        }

        public void set(string varnName, object value)
        {
            varnName = varsPrefix + varnName;
            if (!(value is string))
                value = value.ToString();
            

            if (_useCacheInRam)
            {

                if (!this.cache.ContainsKey(varnName))
                    this.cache[varnName] = new FileVar { name = varnName };
                
                this.cache[varnName].value = (string)value;
                this.cache[varnName].writed = false;


                if (th == null)
                    th = new EasyThread(this.writeToFile, true);
            }
            else
                write(directory + varnName, value is string ? (string)value : value.ToString());
        }
        public void set(string name, VarValue value)
        {
            this.set(name, value.AsString);
        }

        public bool del(string name)
        {
            
            name = directory + StringToFileName(varsPrefix + name);
            this.cache.Remove(name);

            //operações com arquivos devem ser, preferencialmente, realizadas em threads
            Thread trWrt = new Thread(delegate ()
            {
                if (name.ToLower().IndexOf("http://") == 0)
                {
                    int tries = 3;
                    while (tries > 0)
                    {
                        try
                        {
                            httpUtils.httpRequest(name, "", null, "DELETE");
                        }
                        catch { }
                        tries--;
                    }
                }
                else
                {
                    int tries = 5;
                    int currentRetryInterval = 50;
                    while (tries > 0)
                    {
                        try
                        {
                            name = this.StringToFileName(name);
                            waitOne(name);
                            if (System.IO.File.Exists(name))
                            {
                                System.IO.File.Delete(name);
                            }
                            release(name);
                            tries = 0;
                        }
                        catch
                        {
                            Thread.Sleep(currentRetryInterval);
                            currentRetryInterval += 50;
                        }
                        tries--;
                    }
                }
            });
            trWrt.Start();

            return true;
        }


        public bool isAvailable()
        {
            if (directory.ToLower().IndexOf("http://") == 0)
            {
                try
                {
                    httpUtils.httpRequest(directory);
                }
                catch {
                    return false;
                }
            }
            return true;
        }
        public bool exists(string varName)
        {
            varName = varsPrefix + varName;

            if (this.cache.ContainsKey(varName))
                return true; 
            else if (varName.ToLower().IndexOf("http://") == 0)
            {
                int tries = 3;
                varName = directory + varName;
                while (tries > 0)
                {
                    try
                    {
                        //the server can return invalidValue
                        string tmp = httpUtils.httpRequest(varName);
                        if (tmp != invalidValue)
                            return true;
                    }
                    catch { }
                    tries--;
                }
            }
            else
            {
                int tries = 3;
                varName = directory + varName;
                while (tries > 0)
                {
                    try
                    {
                        return System.IO.File.Exists(varName);
                    }
                    catch { }
                    tries--;
                }
            }

            return false;

        }

        public string[] getChilds(string parent)
        {
            parent = StringToFileName(varsPrefix + parent) + "/*";
            List<string> result = new List<string>();
            if (directory.ToLower().IndexOf("http://") == 0)
            {
                int tries = 3;
                while (tries > 0)
                {
                    try
                    {
                        result = httpUtils.httpRequest(directory + parent, "", new string[] { "Accept: text/csv"}, "SEARCH").Split(new char[] {',', ';' }).ToList();
                    }
                    catch { }
                    tries--;
                }
            }
            else
            {
                result = System.IO.Directory.GetFiles(directory, parent + "*").ToList();
                for (int cont = 0; cont < result.Count; cont++)
                    result[cont] = System.IO.Path.GetFileName(result[cont]);

                if (_useCacheInRam)
                {
                    //carrega registros do cache
                    foreach (var curr in cache)
                    {
                        if (curr.Key.ToUpper().Contains(parent.ToUpper()) && !result.Contains(curr.Key))
                            result.Add(curr.Key);
    
                    }
                }
                
            }

            for (int cont = result.Count - 1; cont >= 0; cont--)
                if (result[cont].Contains(".lock"))
                    result.RemoveAt(cont);

            return result.ToArray();
        }

        private void waitOne(string fname)
        {
            string lockFile = fname + ".lock";
            DateTime start = DateTime.Now;

            while (File.Exists(lockFile))
            {
                Thread.Sleep(10);
            }

            File.WriteAllText(lockFile, "locked");
        }

        private void release(string fname)
        {
            string lockFile = fname + ".lock";
            while (true)
            {
                try
                {
                    if (File.Exists(lockFile))
                        File.Delete(lockFile);
                    break;
                }
                catch { }
                Thread.Sleep(10);
            }
        }

    }
}
