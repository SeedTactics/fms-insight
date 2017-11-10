using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineWatch
{
    public class SettingStore : IStoreSettings
    {
        private string _path;
        public SettingStore(string path) { _path = path; }

        public string GetSettings(string ID)
        {
            var f = System.IO.Path.Combine(
                _path,
                System.IO.Path.GetFileNameWithoutExtension(ID))
                + ".json";
            if (System.IO.File.Exists(f))
                return System.IO.File.ReadAllText(f);
            else
                return null;
        }

        public void SetSettings(string ID, string settingsJson)
        {
            var f = System.IO.Path.Combine(
                _path,
                System.IO.Path.GetFileNameWithoutExtension(ID))
                + ".json";
            System.IO.File.WriteAllText(f, settingsJson);
        }
    }
}
