/*
	by: Rafael Tonello (tonello.rafinha@gmail.com)
	
	Version; 1.1.0.0

	History:
		1.0.0.0 -> 24/01/2018-> First version
		1.1.0.0 -> 24/01/2018-> Prefix for variables

*/

using Libs;
using System;
using System.Collections.Generic;
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
        object toLock = new object();
        bool _useCacheInRam = true;
        string directory = "";
        string varsPrefix;

        public Vars(string directory = "", bool useCacheInRam = true, string varsPrefix = "")
        {
            this._useCacheInRam = useCacheInRam;
            this.appPath = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + "\\";
            this.varsPrefix = varsPrefix;
            if (directory == "")
                directory = this.appPath + "\\vars";

            this.directory = directory;

            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

        }
        /// <summary>
        /// Função para armazenar uma variável no sistema. Estas variáveis são persistentes e podem ser recuperadas em outras.
        /// execuções.
        /// </summary>
        /// <param name="name">Nome da variável.</param>
        /// <param name="value">Valor da variável.</param>
        public void _rawSet(string name, string value)
        {
            lock (toLock)
            {
                name = varsPrefix + name;
                if (_useCacheInRam)
                {
                    if ((!this.cache.ContainsKey(name)) || (this.cache[name] == null)) this.cache[name] = new FileVar { name = name };
                    this.cache[name].value = value;
                    this.cache[name].writed = false;


                    if (th == null)
                        th = new EasyThread(this.writeToFile, true);
                }
                else
                    System.IO.File.WriteAllText(directory + "\\" + name, value);
            }
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
                                System.IO.File.WriteAllText(directory + "\\" + name, this.cache.ElementAt(cont).Value.value);
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
        /// <summary>
        /// Função utilizada para recuperar uma variável.
        /// </summary>
        /// <param name="name">Nome da variável.</param>
        /// <returns></returns>
        public string _rawGet(string name)
        {
            lock (toLock)
            {
                name = varsPrefix + name;
                string ret;
                if ((_useCacheInRam) && (cache.ContainsKey(name)))
                    return cache[name].value;
                else
                {
                    int tries = 5;
                    //used to increment the time between the fail and new try
                    int currentRetryInterval = 50;
                    while (tries > 0)
                    {
                        try
                        {
                            if (System.IO.Directory.Exists(directory))
                            {
                                string fName = directory + "\\" + this.StringToFileName(name);
                                if (System.IO.File.Exists(fName))
                                    ret = System.IO.File.ReadAllText(fName);
                                else
                                    ret = invalidValue;
                                if ((!this.cache.ContainsKey(name)) || (this.cache[name] == null)) this.cache[name] = new FileVar { name = name };
                                this.cache[name].value = ret;
                                this.cache[name].writed = true;
                                return ret;
                            }
                        }
                        catch
                        {
                            Thread.Sleep(currentRetryInterval);
                            currentRetryInterval += 50;
                        }
                        tries--;

                    }
                }
                return this.invalidValue;
            }
        }


        public VarValue get(string name, object def)
        {
            VarValue ret = new VarValue { AsString = this._rawGet(name) };
            if (ret.AsString == this.invalidValue)
                ret.AsString = def.ToString();

            return ret;
        }

        public void set(string name, object value)
        {
            this._rawSet(name, value.ToString());
        }

        public bool del(string name)
        {
            lock (toLock)
            {
                name = varsPrefix + name;
                this.cache.Remove(name);

                //operações com arquivos devem ser, preferencialmente, realizadas em threads
                Thread trWrt = new Thread(delegate ()
                {
                    int tries = 5;
                    int currentRetryInterval = 50;
                    while (tries > 0)
                    {
                        try
                        {
                            name = this.StringToFileName(name);
                            if (System.IO.Directory.Exists(directory))
                            {
                                System.IO.File.Delete(directory + "\\" + name);
                            }
                            tries = 0;
                        }
                        catch
                        {
                            Thread.Sleep(currentRetryInterval);
                            currentRetryInterval += 50;
                        }
                        tries--;
                    }
                });
                trWrt.Start();

                return true;
            }
        }

        public void set(string name, VarValue value)
        {
            this._rawSet(name, value.AsString);
        }


        public string[] getChilds(string parent)
        {
            parent = varsPrefix + parent;
            string[] result = System.IO.Directory.GetFiles(directory, parent + "*");
            for (int cont = 0; cont < result.Length; cont++)
                result[cont] = System.IO.Path.GetFileName(result[cont]);

            return result;
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
    }
}
