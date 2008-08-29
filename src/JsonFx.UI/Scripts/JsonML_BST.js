﻿/*global JsonML */
/*
	JsonML_BST.js
	JsonML Browser-Side Templating

	Created: 2008-07-28-2337
	Modified: 2008-08-29-0056

	Released under an open-source license:
	http://jsonml.org/License.htm

    This file creates a JsonML.BST type containing this method:

	new JsonML.BST(template).dataBind(data)
*/

/* namespace JsonML */
if ("undefined" === typeof JsonML) {
	window.JsonML = {};
}

JsonML.BST = function(/*JsonML+BST*/ jbst) {
	var self = this;

	// unique counter for generated method names
	var g = 0;

	// recursively applies dataBind to all nodes of the template graph
	// NOTE: it is very important to replace each node with a copy,
	// otherwise it destroys the original template.
	/*object*/ function db(/*JsonML+BST*/ t, /*object*/ d, /*int*/ n) {
		// process JsonML+BST node
		if (t) {
			if ("function" === typeof t) {
				// temporary method name using a counter to
				// avoid collisions when recursively calling
				var m = "$jbst_"+(g++)+"";
				try {
					// setup context for code block
					self[m] = t;
					self.data = d;
					self.index = isFinite(n) ? Number(n) : -1;
					// execute in the context of template as "this"
					return self[m]();
				} finally {
					g--;
					delete self[m];
					delete self.data;
					delete self.index;
				}
			}

			var o;
			if (t instanceof Array) {
				// output array
				o = [];
				for (var i=0; i<t.length; i++) {
					// result
					var r = db(t[i], d, n);
					if (r instanceof Array && r.length && r[0] === "") {
						// result was multiple JsonML trees
						r.shift();
						o = o.concat(r);
					} else if ("object" === typeof r) {
						// result was a JsonML tree
						o.push(r);
					} else if ("undefined" !== typeof r && r !== null) {
						// must convert to string or JsonML will discard
						o.push(String(r));
					}
				}
				return o;
			}

			if ("object" === typeof t) {
				// output object
				o = {};
				// for each property in node
				for (var p in t) {
					if (t.hasOwnProperty(p)) {
						o[p] = db(t[p], d, n);
					}
				}
				return o;
			}
		}

		// rest are value types, so return node directly
		return t;
	}

	// the publicly exposed instance method
	// combines JsonML+BST and JSON to produce JsonML
	/*JsonML*/ this.dataBind = function(/*object*/ data, /*int*/ index) {
		if (data instanceof Array) {
			// create a document fragment to hold list
			var o = [""];

			for (var i=0; i<data.length; i++) {
				// apply template to each item in array
				o.push(db(jbst, data[i], i));
			}
			return o;
		} else {
			// data is singular to apply template once
			return db(jbst, data, index);
		}
	};
};
