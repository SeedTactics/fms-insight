const ps = require("prelude-ts");
module.exports = {
  test(val) {
    return val instanceof ps.Vector || val instanceof ps.HashMap || val instanceof ps.HashSet;
  },

  print(val, serialize, indent) {
    if (val instanceof ps.Vector) {
      return serialize(val.toArray());
    } else if (val instanceof ps.HashMap) {
      return serialize(val.toJsMap(x => (typeof x === "string" ? x : serialize(x))));
    } else if (val instanceof ps.HashSet) {
      return serialize(val.toArray().sort());
    }
  }
};
