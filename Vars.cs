/*
	by: Rafael Tonello (tonello.rafinha@gmail.com)
	
	Version; 3.0.0.1

	History:
		1.0.0.0 -> 24/01/2018-> First version
		1.1.0.0 -> 24/01/2018-> Prefix for variables
        2.0.0.0 -> 24/01/2018-> Support for http requests (GET, POST and DELETE)
        2.0.1.0 -> 24/01/2018-> Added semaphore resource to access files
        2.0.2.0 -> 09/02/2018-> Added isAvailable function and remove .lock entries from getChilds result
        2.0.2.1 -> 09/02/2018-> Fixed a bug that throwing an exception when the .lock entries were removed from the getChilds result
		3.0.0.0 -> 06/08/2018-> Now, the library can work with subfolders instead just files with object notation names (if files with object notation name is found, their will, automatically, converted to new system)
		3.0.0.1 -> 12/09/2018-> Solved a problem with del function (the folders that were empty, were not being deleted)

*/

using Libs;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        //this property indicates that the class can use subfolders or just files with object natation names.
        //This configuratio can sugestted in the constructor (if any folder is found inside the Vars folder, this
        //property will receive the value "true"
        bool useSubFolders = false;

        HttpUtils httpUtils = new HttpUtils();
        //this semaphore is used to make available a semphore to developer. Is just for developer no having to instanciate a sempahroe externaly and
        //so he have a semaphore for all places where he will use a same instance of vars.
        Semaphore locker = new Semaphore(1, int.MaxValue);

        public Vars(string directory = "", bool useCacheInRam = true, string varsPrefix = "", bool useSubFolders = true)
        {
            this.useSubFolders = useSubFolders;

            this._useCacheInRam = useCacheInRam;
            this.appPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).Replace("\\", "/") + "/";
            this.varsPrefix = varsPrefix;
            if (directory == "")
                directory = this.appPath + "vars";




            if (directory.IndexOf("http://") == 0)
                directory = directory.Replace("\\", "/");
            else
                createDirectory(directory);

            if (directory[directory.Length - 1] != '/')
                directory += "/";

            this.directory = directory;

            //fore retrocompatibility, try conver all objectnotation files to format (using subdirectories)
            ConvertOldFolderFormat();


        }

        public static Semaphore SemaphoreConvert = new Semaphore(1, int.MaxValue);
        private void ConvertOldFolderFormat()
        {
            SemaphoreConvert.WaitOne();
            if (useSubFolders)
            {
                var oldFiles = Directory.GetFiles(directory);
                foreach (var curr in oldFiles)
                {
                    if (curr.Contains("start")) ;
                    this.set(Path.GetFileName(curr), File.ReadAllText(curr));
                    File.Delete(curr);
                }

            }
            SemaphoreConvert.Release();
            flush();
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
            fName = getValidVarName(fName);
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

                if (this.useSubFolders)
                    fName = fName.Substring(0, directory.Length + 1) + fName.Substring(directory.Length + 1).Replace('.', '/');
                fName = fName.Replace("\\", "/");

                createDirectory(Path.GetDirectoryName(fName));



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
            fName = getValidVarName(fName);
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
                string oldFolderFormatFileName = fName;

                if (this.useSubFolders)
                    fName = fName.Substring(0, directory.Length + 1) + fName.Substring(directory.Length + 1).Replace('.', '/');
                fName = fName.Replace("\\", "/");

                while (tries > 0)
                {
                    try
                    {

                        if (System.IO.File.Exists(fName))
                        {
                            waitOne(fName);
                            string ret = System.IO.File.ReadAllText(fName);
                            release(fName);
                            return ret;
                        }
                        else
                        {
                            tries = 0;
                        }
                    }
                    catch
                    {
                        try { release(fName); } catch { }
                    }
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
                                string name = getValidVarName(this.cache.ElementAt(cont).Key);
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
                    ret = read(directory + name);
                    if ((ret != invalidValue) && ((!this.cache.ContainsKey(name)) || (this.cache[name] == null))) this.cache[name] = new FileVar { name = name };
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

        public bool del(string name, bool delChilds = false, bool await = false)
        {
            this.cache.Remove(varsPrefix + name);

            bool done = false;

            //operações com arquivos devem ser, preferencialmente, realizadas em threads
            Thread trWrt = new Thread(delegate ()
            {
                //delete childs
                if (delChilds)
                {
                    var childs = this.getChilds(name);
                    foreach (var curr in childs)
                        this.del(curr, true, await);
                }

                name = varsPrefix + name;

                name = directory + name;

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
                    name = getValidVarName(name);

                    if (this.useSubFolders)
                        name = name.Substring(0, directory.Length + 1) + name.Substring(directory.Length + 1).Replace('.', '/');

                    name = name.Replace("\\", "/");

                    //createDirectory(Path.GetDirectoryName(name));

                    while (name.Contains('/'))
                    {
                        int tries = 5;
                        int currentRetryInterval = 50;
                        if (System.IO.File.Exists(name))
                        {
                            while (tries > 0)
                            {
                                try
                                {
                                    {
                                        waitOne(name);
                                        System.IO.File.Delete(name);
                                        release(name);
                                    }
                                    tries = 0;
                                }
                                catch
                                {
                                    Thread.Sleep(currentRetryInterval);
                                    currentRetryInterval += 50;
                                }
                                release(name);
                                tries--;
                            }
                        }
                        else if (System.IO.Directory.Exists(name))
                        {
                            try
                            {
                                if (Directory.GetFiles(name).Length > 0)
                                {
                                    name = "";
                                    break;
                                }
                            }
                            catch { }
                            try
                            {
                                if (Directory.GetDirectories(name).Length > 0)
                                {
                                    name = "";
                                    break;
                                }
                            }
                            catch { }

                            if (name.Length <= directory.Length)
                            {
                                name = "";
                                break;
                            }

                            try
                            {
                                Directory.Delete(name);
                            }
                            catch { }
                        }
                        name = name.Substring(0, name.LastIndexOf('/'));
                    }
                }

                done = true;
            });
            trWrt.Start();


            if (await)
            {
                while (!done)
                    Thread.Sleep(10);
            }
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
                catch
                {
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
                varName = directory + varName;

                if (this.useSubFolders)
                    varName = varName.Substring(0, directory.Length + 1) + varName.Substring(directory.Length + 1).Replace('.', '/');

                varName = varName.Replace("\\", "/");

                createDirectory(Path.GetDirectoryName(varName));

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
            parent = varsPrefix + parent;
            List<string> result = new List<string>();
            if (directory.ToLower().IndexOf("http://") == 0)
            {
                int tries = 3;
                while (tries > 0)
                {
                    try
                    {
                        result = httpUtils.httpRequest(directory + parent + "/*", "", new string[] { "Accept: text/csv" }, "SEARCH").Split(new char[] { ',', ';' }).ToList();
                    }
                    catch { }
                    tries--;
                }
            }
            else
            {
                parent = getValidVarName(parent);
                if (this.useSubFolders)
                    parent = parent.Replace(".", "/");
                List<string> folders = new List<string>();

                folders.Add(directory + parent);

                while (folders.Count > 0)
                {
                    string currFolder = folders[0];
                    folders.RemoveAt(0);

                    currFolder = currFolder.Replace("\\", "/");

                    if (Directory.Exists(currFolder))
                    {
                        result.AddRange(Directory.GetFiles(currFolder));
                        folders.AddRange(Directory.GetDirectories(currFolder));
                    }
                }



                for (int cont = 0; cont < result.Count; cont++)
                    result[cont] = result[cont].Substring(directory.Length).Replace('/', '.').Replace('\\', '.');

                for (int cont = result.Count - 1; cont >= 0; cont--)
                    if (result[cont].Contains(".lock"))
                        result.RemoveAt(cont);

                if (_useCacheInRam)
                {
                    //carrega registros do cache
                    foreach (var curr in cache)
                    {
                        if (curr.Key.ToUpper().Contains(parent.ToUpper()) && !result.Contains(curr.Key))
                            result.Add(curr.Key);
                    }
                }

                //remove prefixNames
                for (int cont = 0; cont < result.Count; cont++)
                    result[cont] = result[cont].Substring(varsPrefix.Length);

            }



            return result.ToArray();
        }

        /// <summary>
        /// If a lock file exists, waits for her exclusion. After, create a new lock file.
        /// </summary>
        /// <param name="fname">File to lock</param>
        /// <param name="timeout">Max wait timeout. User 0 (zero) to timeout = forever</param>
        private void waitOne(string fname, int timeout = 10000)
        {
            string lockFile = fname;

            lockFile = getValidVarName(lockFile);
            DateTime start = DateTime.Now;

            //if (this.useSubFolders)
            //    lockFile = lockFile.Substring(0, directory.Length + 1) + lockFile.Substring(directory.Length + 1).Replace('.', '/');
            lockFile = lockFile.Replace("\\", "/");
            createDirectory(Path.GetDirectoryName(lockFile));

            lockFile += ".lock";

            while (File.Exists(lockFile))
            {
                Thread.Sleep(10);
                if ((timeout > 0) && (DateTime.Now.Subtract(start).TotalMilliseconds >= timeout))
                    break;
            }

            File.WriteAllText(lockFile, "locked");
        }

        private void release(string fname)
        {
            string lockFile = fname;

            lockFile = getValidVarName(lockFile);

            if (this.useSubFolders)
                lockFile = lockFile.Substring(0, directory.Length + 1) + lockFile.Substring(directory.Length + 1).Replace('.', '/');

            lockFile = lockFile.Replace("\\", "/");

            lockFile += ".lock";

            if (!Directory.Exists(Path.GetDirectoryName(lockFile)))
                return;

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

        private void createDirectory(string directoryName)
        {
            directoryName = getValidVarName(directoryName);

            directoryName = directoryName.Replace('\\', '/');
            if (directoryName[directoryName.Length - 1] == '/')
                directoryName = directoryName.Substring(0, directoryName.Length - 1);

            List<string> folderNames = directoryName.Split('/').ToList();

            string currName = folderNames[0];
            folderNames.RemoveAt(0);
            do
            {
                currName = currName + "/" + folderNames[0];
                folderNames.RemoveAt(0);

                if (File.Exists(currName))
                    File.Delete(currName);
            } while (folderNames.Count > 0);

            if (!Directory.Exists(directoryName))
                Directory.CreateDirectory(directoryName);

        }

        public void flush()
        {
            if (th == null)
                th = new EasyThread(this.writeToFile, true);

            th.pause();

            writeToFile(th, null);

            th.resume();
        }

        private string getValidVarName(string varName)
        {
            string ret = "";
            int index = 0;
            foreach (var c in varName)
            {
                if ("abcdefghijklmnopqrstuvxywz_/.-0123456789, ()\\".ToUpper().Contains((c + "").ToUpper()))
                    ret += c;
                else if ((c == ':') && (index == 1))
                    ret += c;

                index++;
            }

            return ret;
        }

    }
}
