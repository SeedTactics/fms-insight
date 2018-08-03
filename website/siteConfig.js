/**
 * Copyright (c) 2017-present, Facebook, Inc.
 *
 * This source code is licensed under the MIT license.
 */
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

// See https://docusaurus.io/docs/site-config.html for all the possible
// site configuration options.

/* List of projects/orgs using your project for the users page */
const users = [
  {
    caption: "Applied Engineering",
    image: '/img/users/applied.jpg',
    infoLink: 'http://www.appliedeng.com',
    pinned: true
  },
  {
    caption: "Blackhawk Engineering",
    image: '/img/users/blackhawk.png',
    infoLink: 'http://www.blackhawkengineering.com/',
    pinned: true
  },
  {
    caption: 'Caterpillar',
    image: '/img/users/cat.jpg',
    infoLink: 'https://www.caterpillar.com',
    pinned: true,
  },
  {
    caption: 'Ditch Witch',
    image: '/img/users/ditchwitch.jpg',
    infoLink: 'https://www.ditchwitch.com/',
    pinned: true
  },
  {
    caption: "Geater Machining & Manufacturing",
    image: '/img/users/geater.jpg',
    infoLink: 'http://www.geater.com/',
    pinned: true
  },
  {
    caption: "John Deere",
    image: '/img/users/deere.png',
    infoLink: 'https://www.deere.com',
    pinned: true
  },
  {
    caption: "Parker Hannifin",
    image: '/img/users/parker.svg',
    infoLink: 'https://www.parker.com',
    pinned: true
  }
];

const siteConfig = {
  title: 'SeedTactic FMS Insight' /* title for your website */,
  tagline: 'Open source data analytics and monitoring for flexible manfuacturing systems',
  url: 'https://fms-insight.seedtactics.com' /* your website url */,
  baseUrl: '/' /* base url for your project */,
  cleanUrl: true,
  // For github.io type URLs, you would set the url and baseUrl like:
  //   url: 'https://facebook.github.io',
  //   baseUrl: '/test-site/',

  // Used for publishing and more
  projectName: 'fms-insight',
  organizationName: 'blackmaple',
  // For top-level user or org sites, the organization is still the same.
  // e.g., for the https://JoelMarcey.github.io site, it would be set like...
  //   organizationName: 'JoelMarcey'

  // For no header links in the top nav bar -> headerLinks: [],
  headerLinks: [
    {doc: 'overview', label: 'Docs'},
  ],

  // If you have users set above, you add it here:
  users,

  /* path to images for header/footer */
  headerIcon: 'img/seedtactics-logo.svg',
  footerIcon: 'img/seedtactics-logo.svg',
  favicon: 'img/favicon.ico',

  /* colors for website */
  colors: {
    primaryColor: '#388E3C',
    secondaryColor: '#DBA72E',
  },

  /* custom fonts for website */
  /*fonts: {
    myFont: [
      "Times New Roman",
      "Serif"
    ],
    myOtherFont: [
      "-apple-system",
      "system-ui"
    ]
  },*/

  // This copyright info is used in /core/Footer.js and blog rss/atom feeds.
  copyright:
    'Copyright © ' +
    new Date().getFullYear() +
    ' Black Maple Software, LLC',

  highlight: {
    // Highlight.js theme to use for syntax highlighting in code blocks
    theme: 'default',
  },

  // Add custom scripts here that would be placed in <script> tags
  scripts: [],

  // Add stylesheets here that would be placed in <link> tags
  stylesheets: [],

  /* On page navigation for the current documentation page */
  onPageNav: 'separate',

  // You may provide arbitrary config keys to be used as needed by your
  // template. For example, if you need your repo's URL...
  //   repoUrl: 'https://github.com/facebook/test-site',
};

module.exports = siteConfig;
