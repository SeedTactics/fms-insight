/**
 * Copyright (c) 2017-present, Facebook, Inc.
 *
 * This source code is licensed under the MIT license.
 */
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

const React = require("react");

const CompLibrary = require("../../core/CompLibrary.js");
const MarkdownBlock = CompLibrary.MarkdownBlock; /* Used to read markdown */
const Container = CompLibrary.Container;
const GridBlock = CompLibrary.GridBlock;

const siteConfig = require(process.cwd() + "/siteConfig.js");

function imgUrl(img) {
  return siteConfig.baseUrl + "img/" + img;
}

function screenshotUrl(img) {
  return siteConfig.baseUrl + "screenshots/" + img;
}

function assetUrl(img) {
  return siteConfig.baseUrl + "docs/assets/" + img;
}

function docUrl(doc, language) {
  return siteConfig.baseUrl + "docs/" + (language ? language + "/" : "") + doc;
}

function pageUrl(page, language) {
  return siteConfig.baseUrl + (language ? language + "/" : "") + page;
}

class Button extends React.Component {
  render() {
    return (
      <div className="pluginWrapper buttonWrapper">
        <a className="button" href={this.props.href} target={this.props.target}>
          {this.props.children}
        </a>
      </div>
    );
  }
}

Button.defaultProps = {
  target: "_self",
};

const SplashContainer = (props) => (
  <div className="homeContainer">
    <div className="homeSplashFade">
      <div className="wrapper homeWrapper">{props.children}</div>
    </div>
  </div>
);

const ProjectTitle = (props) => (
  <h2 className="projectTitle">
    {siteConfig.title}
    <small>{siteConfig.tagline}</small>
  </h2>
);

const PromoSection = (props) => (
  <div className="section promoSection">
    <div className="promoRow">
      <div className="pluginRowBlock">{props.children}</div>
    </div>
  </div>
);

class HomeSplash extends React.Component {
  render() {
    let language = this.props.language || "";
    return (
      <SplashContainer>
        <div className="inner">
          <ProjectTitle />
          <PromoSection>
            {/* <Button href="#try">Try It Out</Button> */}
            <Button href={docUrl("getting-started.html", language)}>
              Get Started
            </Button>
            <Button href="/demo/demo.html">View Demo</Button>
            <Button href="https://www.seedtactics.com/contact">
              Contact Sales
            </Button>
          </PromoSection>
        </div>
      </SplashContainer>
    );
  }
}

const Block = (props) => (
  <Container
    padding={["bottom", "top"]}
    id={props.id}
    background={props.background}
  >
    <GridBlock
      align={props.align || "center"}
      contents={props.children}
      layout={props.layout}
    />
  </Container>
);

const Features = (props) => (
  <Block layout="fourColumn">
    {[
      {
        content:
          "Collect machine cycles, pallet cycles, load/unload operations, " +
          "and planned jobs, and then view efficiency and cost/piece reports to improve productivity.",
        image: imgUrl("assessment.svg"),
        imageAlign: "top",
        title: "Data Analytics",
      },
      {
        content:
          "View load instructions, inspection decisions, workorder assignment, and part serials " +
          " on the factory floor.",
        image: imgUrl("search.svg"),
        imageAlign: "top",
        title: "Station Monitoring",
      },
      {
        content:
          "Track and record parts by serial as they transition from manual handling to the automation" +
          " sytem and back again.",
        image: imgUrl("label.svg"),
        imageAlign: "top",
        title: "Part Tracking",
      },
    ]}
  </Block>
);

const FeatureCallout = (props) => (
  <div
    className="productShowcaseSection paddingBottom"
    style={{ textAlign: "center" }}
  >
    <h2>Automation Management And Performance Improvement</h2>
    <MarkdownBlock>
      FMS Insight is used by industrial engineers, floor managers, and operators
      to enhance an automated handling system in a flexible machining cell. We
      have observed that an event log allows process actions such as inspections
      which guarantee each path is periodically inspected. Data integration with
      the cell controller simplifies operations and leads to higher efficiency.
    </MarkdownBlock>
  </div>
);

const traits = [
  {
    content:
      "FMS Insight provides easily accessible targeted pages for operators, engineers, supervisors, and management. " +
      "Each page displays information about the cell targeted at the specific user; for example, the operator page shows " +
      "the load and unload instructions for the current pallet.",
    image: screenshotUrl("insight-loadstation-small.jpg"),
    title: "Broadcast Info Throughout the Factory",
  },
  {
    content:
      "FMS Insight provides a touchscreen friendly view of information at the load station, including " +
      "what part type to load, what part type to unload, serial assignment, inspection decisions, " +
      "load instructions, and workorder assignment.",
    image: imgUrl("load-station.jpg"),
    title: "Load/Unload Instructions",
  },
  {
    content:
      "FMS Insight can generate a unique serial for each piece of material and then print a label or scribe it on the part. " +
      "FMS Insight can then track material throughout the entire process and display " +
      "historical data about a specific serial.",
    image: screenshotUrl("insight-inspection-sankey-only.jpg"),
    title: "Serial Tracking",
  },
  {
    content:
      "FMS Insight can facilitate inspection signaling where each combination of " +
      "pallet and machine is guranteed to be periodically inspected.  FMS Insight provides " +
      "a view of the serial's events at the inspection stand which allows the operator to view the pallet, machine, " +
      "and date/time of the part being inspected.  Finally, quality engineers can view recent failed inspections and " +
      "lookup failed serials.",
    image: screenshotUrl("insight-inspection-details.jpg"),
    title: "Inspections and Quality",
  },
  {
    content:
      "FMS Insight uses statistics to automatically calculate the tool usage per program and provides a " +
      "report showing the expected use from the currently scheduled parts and compares that to the current remaining life " +
      " of tools currently in the machines.",
    image: assetUrl("insight-tool-report.png"),
    title: "Tool & Program Management",
  },
  {
    content:
      "FMS Insight provides a status dashboard for supervisors which shows an overview of the daily operations of the cell. " +
      "The dashboard shows the progress on the current jobs, recent statistical outlier load and machine events, machine and labor " +
      "OEE for the previous week, and more.",
    image: screenshotUrl("insight-dashboard-jobs.jpg"),
    title: "Supervisor Dashboard",
  },
  {
    content:
      "Using the event log, FMS Insight provides reports and charts targeted at iteratively improving " +
      "the efficiency of the cell.  These reports have been built up over decades of expierence to highlight the " +
      "places where small changes to pallet or machine assignments can lead to large performance improvements",
    image: screenshotUrl("insight-station-cycle-graph-only.jpg"),
    title: "Efficiency Improvement",
  },
  {
    content:
      "Using the event log, FMS Insight provides a monthly cost/piece report showing a breakdown of the " +
      "machining and labor cost for each part.  The cost/piece can be used to validate that iterative operational changes " +
      "are actually improvements, helps plan future capital investments, and assists ordering and sales quote future work.",
    image: screenshotUrl("insight-part-cost.png"),
    title: "Cost/Piece",
  },
  {
    content:
      "FMS Insight provides a screen which allows an operator to assign a piece of material to a workorder.  This " +
      "goes into the event log and FMS Insight can track how many parts have been assigned to a workorder. " +
      "If serials/barcodes are used, the individual serials are tracked in the workorder to allow ERP cost calculations at " +
      "the workorder level.",
    image: screenshotUrl("insight-wash-screen.jpg"),
    title: "Workorder Assignment",
  },
  {
    content:
      "FMS Insight uses the event log to provide a touchscreen friendly view of all the material in the cell. " +
      "This is helpful when material enters and leaves the automated handling system, such as between part processes or " +
      "if parts are transfered from a lathe cell to a horizontal cell.",
    image: screenshotUrl("insight-all-material.jpg"),
    title: "In-Process Material Tracking",
  },
  {
    content:
      "FMS Insight provides an API which can be used to create parts, pallets, jobs, and schedules in the cell " +
      "controller.  Each project and ERP is different, so FMS Insight does not directly create the data.  Instead, " +
      "FMS Insight provides an API which easily allows the ERP software to translate the orders in the ERP into " +
      "data in the cell controller.  The API uses JSON and a REST-like HTTP interface, and is described fully as an " +
      "OpenAPI specification.",
    image: screenshotUrl("insight-swagger.jpg"),
    title: "Cell Control Data",
  },
];

const Traits = (props) => (
  <div>
    {traits.map((t, idx) => (
      <Block
        align="left"
        key={idx}
        background={idx % 2 === 0 ? "light" : undefined}
      >
        {[
          {
            content: t.content,
            image: t.image,
            title: t.title,
            imageAlign: idx % 2 === 0 ? "right" : "left",
          },
        ]}
      </Block>
    ))}
  </div>
);

const Showcase = (props) => {
  if ((siteConfig.users || []).length === 0) {
    return null;
  }
  const showcase = siteConfig.users
    .filter((user) => {
      return user.pinned;
    })
    .map((user, i) => {
      return (
        <a href={user.infoLink} key={i}>
          <img src={user.image} alt={user.caption} title={user.caption} />
        </a>
      );
    });

  return (
    <div className="productShowcaseSection paddingBottom">
      <h2>{"Who's Using This?"}</h2>
      <p>This software is used by all these companies:</p>
      <div className="logos">{showcase}</div>
      {/*
      <div className="more-users">
        <a className="button" href={pageUrl('users.html', props.language)}>
          More {siteConfig.title} Users
        </a>
      </div>
      */}
    </div>
  );
};

class Index extends React.Component {
  render() {
    let language = this.props.language || "";

    return (
      <div>
        <HomeSplash language={language} />
        <div className="mainContainer">
          <Features />
          <FeatureCallout />
          <Traits />
          <Showcase language={language} />
        </div>
      </div>
    );
  }
}

module.exports = Index;
