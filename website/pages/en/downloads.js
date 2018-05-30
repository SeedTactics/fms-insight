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

const React = require('react');
const CompLibrary = require('../../core/CompLibrary.js');
const child_process = require('child_process');
const format = require('date-fns').format;

class DownloadLink extends React.Component {
  render() {
    let prefix = this.props.prefix;
    let tag = child_process.execFileSync("hg", [
      "id", "-t", "-r", ".^"
    ]).toString();
    if (!tag || !tag.startsWith(prefix)) {
      tag = child_process.execFileSync("hg", [
        "id", "-t", "-r", "ancestors(.) and tag('re:" + prefix + "')"
      ]).toString();
    }
    let ver = tag.replace(prefix + "-", "");
    let dateS = child_process.execFileSync("hg", [
      "id", "-r", tag, "--template", "{date|rfc3339date}"
    ]);
    let date = new Date(dateS);
    let nameUpper = prefix.charAt(0).toUpperCase() + prefix.slice(1);
    return (
      <span>
        <a
          href={
            "https://seedtactics-downloads.s3.amazonaws.com/" +
            "fms-insight/" + prefix + "/" + ver + "/" +
            "FMS%20Insight%20" + nameUpper + "%20Install.msi"
          }
        >
          FMS Insight {nameUpper} Install
        </a>
        - Version {ver}, {format(date, "dddd MMMM D, YYYY")}
      </span>
    );
  }
}

class Downloads extends React.Component {
  render() {
    return (
      <div>
        <ul>
          <li><DownloadLink prefix="mazak"/></li>
          <li><DownloadLink prefix="makino"/></li>
        </ul>
      </div>
    );
  }
}

module.exports = Downloads;