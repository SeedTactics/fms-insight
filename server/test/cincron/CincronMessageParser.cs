/* Copyright (c) 2019, John Lenz

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Cincron;
using FluentAssertions;

namespace MachineWatchTest.Cincron
{
  public class MessageParseTest
  {
    private static Newtonsoft.Json.JsonSerializerSettings jsonSettings = new Newtonsoft.Json.JsonSerializerSettings()
    {
      TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All
    };


    [Fact]
    public void ParseAllMessages()
    {
      var msg = MessageParser.ExtractMessages("../../../cincron/sample-messages", 0, "");
      var expected = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CincronMessage>>(System.IO.File.ReadAllText("../../../cincron/sample-messages.json"), jsonSettings);
      msg.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void ParseMiddle()
    {
      var msg = MessageParser.ExtractMessages("../../../cincron/sample-messages", 363143, "Jan 25 01:13:12 ABCDEF CINCRON[5889]: stn002--I10402:Control Data for Work Unit 22 Updated   [STEP_NO = 2]");
      var expected = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CincronMessage>>(System.IO.File.ReadAllText("../../../cincron/sample-messages.json"), jsonSettings);
      msg.Should().BeEquivalentTo(expected.GetRange(2533, 2233));
    }

    [Fact]
    public void Rollover()
    {
      var msg = MessageParser.ExtractMessages("../../../cincron/sample-messages", 363143, "bad match");
      var expected = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CincronMessage>>(System.IO.File.ReadAllText("../../../cincron/sample-messages.json"), jsonSettings);
      // since offset doesn't match, should load everything from beginning
      msg.Should().BeEquivalentTo(expected);
    }
  }
}