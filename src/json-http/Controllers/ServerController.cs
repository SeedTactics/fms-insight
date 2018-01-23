/* Copyright (c) 2018, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BlackMaple.MachineWatchInterface;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/v1/[controller]")]
    public class serverController : ControllerBase
    {
        private Plugin _plugin;
        private Dictionary<string, string> _memorySettings;

        public serverController(Plugin p)
        {
            _plugin = p;
            if (string.IsNullOrEmpty(p.SettingsPath))
                _memorySettings = new Dictionary<string, string>();
        }

        [HttpGet("plugin")]
        public PluginInfo PluginInfo()
        {
            return _plugin.PluginInfo;
        }

        [HttpGet("settings/{id}")]
        public string GetSettings(string id)
        {
            if (_memorySettings != null)
            {
                if (_memorySettings.ContainsKey(id))
                    return _memorySettings[id];
                else
                    return null;

            } else {
                var f = System.IO.Path.Combine(
                    _plugin.SettingsPath,
                    System.IO.Path.GetFileNameWithoutExtension(id))
                    + ".json";
                if (System.IO.File.Exists(f))
                    return System.IO.File.ReadAllText(f);
                else
                    return null;
            }
        }

        [HttpPut("settings/{id}")]
        public void SetSetting(string id, [FromBody] string setting)
        {
            if (_memorySettings != null)
            {
                _memorySettings[id] = setting;
            } else {
                var f = System.IO.Path.Combine(
                    _plugin.SettingsPath,
                    System.IO.Path.GetFileNameWithoutExtension(id))
                    + ".json";
                System.IO.File.WriteAllText(f, setting);
            }
        }
    }
}