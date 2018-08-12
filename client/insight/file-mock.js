const path = require('path');
const fs = require("fs");

module.exports = {
  process(src, filename, config, options) {
    const ct = fs.readFileSync(filename, "utf8");
    return 'module.exports = ' + JSON.stringify(ct) + ';';
  },
};