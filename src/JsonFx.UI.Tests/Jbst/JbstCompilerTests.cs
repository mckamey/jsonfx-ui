#region License
/*---------------------------------------------------------------------------------*\

	Distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2010 Stephen M. McKamey

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in
	all copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
	THE SOFTWARE.

\*---------------------------------------------------------------------------------*/
#endregion License

using System;
using System.IO;
using System.Text;

using Xunit;

using Assert=JsonFx.AssertPatched;

namespace JsonFx.Jbst
{
	public class JbstCompilerTests
	{
		#region Constants

		private const string TraitName = "JBST";
		private const string TraitValue = "Compiler";

		#endregion Constants

		#region Foo Tests

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_MyZebraList_RendersJbst()
		{
			var input =
@"<%@ Control Name=""Foo.MyZebraList"" Language=""JavaScript"" %>

<script type=""text/javascript"">

	/* private members ------------------------------------------ */

	/*int*/ function digits(/*int*/ n) {
		return (n < 10) ? '0' + n : n;
	}

	/* public members ------------------------------------------- */

	// use the item index to alternate colors and highlight
	/*string*/ this.zebraStripe = function(/*bool*/ selected, /*int*/ index, /*int*/ count) {
		var css = [ ""item"" ];
		if (index % 2 === 0) {
			css.push(""item-alt"");
		}
		if (selected) {
			css.push(""item-selected"");
		}
		return css.join("" "");
	};

	/*string*/ this.formatTime = function(/*Date*/ time) {
		return time.getHours() + ':' + digits(time.getMinutes()) + ':' + digits(time.getSeconds());
	};

</script>

<div class=""example"">
	<h2><%= this.data.title %> as of <%= this.formatTime(this.data.timestamp) %>!</h2>
	<p><%= this.data.description %></p>
	<ul class=""items"" jbst:visible=""<%= this.data.children.length > 0 %>"">

		<!-- anonymous inner template -->
		<jbst:control data=""<%= this.data.children %>"">
			<!-- populate list item for each item of the parent's children property -->
			<li class=""<%= Foo.MyZebraList.zebraStripe(this.data.selected, this.index, this.count) %>"">
				<%= this.data.label %> (<%= this.index+1 %> of <%= this.count %>)
			</li>
		</jbst:control>

	</ul>
</div>";

			var expected =
@"/*global JsonML */

/* namespace Foo */
var Foo;
if (""undefined"" === typeof Foo) {
	Foo = {};
}

Foo.MyZebraList = JsonML.BST([
	""div"",
	{
		""class"" : ""example""
	},
	"" "",
	[
		""h2"",
		function() {
	return this.data.title;
},
		"" as of "",
		function() {
	return this.formatTime(this.data.timestamp);
},
		""!""
	],
	"" "",
	[
		""p"",
		function() {
	return this.data.description;
}
	],
	"" "",
	[
		""ul"",
		{
			""class"" : ""items"",
			""jbst:visible"" : function() {
	return this.data.children.length > 0;
}
		},
		"" "",
		""""/* anonymous inner template */,
		"" "",
		function() {
	return JsonML.BST([
	"""",
	""""/* populate list item for each item of the parent's children property */,
	"" "",
	[
		""li"",
		{
			""class"" : function() {
	return Foo.MyZebraList.zebraStripe(this.data.selected, this.index, this.count);
}
		},
		"" "",
		function() {
	return this.data.label;
},
		"" ("",
		function() {
	return this.index+1;
},
		"" of "",
		function() {
	return this.count;
},
		"") ""
	],
	"" ""
]).dataBind(this.data.children, this.index, this.count);
},
		"" ""
	],
	"" ""
]);
// initialize template in the context of ""this""
(function() {
	/* private members ------------------------------------------ */

	/*int*/ function digits(/*int*/ n) {
		return (n < 10) ? '0' + n : n;
	}

	/* public members ------------------------------------------- */

	// use the item index to alternate colors and highlight
	/*string*/ this.zebraStripe = function(/*bool*/ selected, /*int*/ index, /*int*/ count) {
		var css = [ ""item"" ];
		if (index % 2 === 0) {
			css.push(""item-alt"");
		}
		if (selected) {
			css.push(""item-selected"");
		}
		return css.join("" "");
	};

	/*string*/ this.formatTime = function(/*Date*/ time) {
		return time.getHours() + ':' + digits(time.getMinutes()) + ':' + digits(time.getSeconds());
	};
}).call(Foo.MyZebraList);";

			var actual = new JbstCompiler().Compile("~/Foo.jbst", input);

			Assert.Equal(expected, actual);
		}

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_MyJbstControl_RendersJbst()
		{
			var input =
@"<%@ Control Name=""MyApp.MyJbstControl"" Language=""JavaScript"" %>

<script type=""text/javascript"">
/* initialization code block, executed only once as control is loaded */
this.generateValue = function() {
return new Date().toString();
};

this.myInitTime = this.generateValue();
</script>

<%
/* data binding code block, executed each time as control is data bound */
this.myBindTime = this.generateValue();
%>

<%-- JBST Comment --%>
<span style=""color:red""><%= this.myBindTime /* data binding expression */ %></span>
<span style=""color:green""><%= this.myInitTime /* data binding expression */ %></span>

<!-- HTML Comment -->
<span style=""color:blue""><%$ Resources: localizationKey %><%-- JBST extension --%></span>";

			var expected =
@"/*global JsonML */

/* namespace MyApp */
var MyApp;
if (""undefined"" === typeof MyApp) {
	MyApp = {};
}

MyApp.MyJbstControl = JsonML.BST([
	"""",
	"" "",
	function() {
				/* data binding code block, executed each time as control is data bound */
this.myBindTime = this.generateValue();
			},
	"" "",
	[
		""span"",
		{
			style : ""color:red""
		},
		function() {
	return this.myBindTime /* data binding expression */;
}
	],
	"" "",
	[
		""span"",
		{
			style : ""color:green""
		},
		function() {
	return this.myInitTime /* data binding expression */;
}
	],
	"" "",
	""""/* HTML Comment */,
	"" "",
	[
		""span"",
		{
			style : ""color:blue""
		},
		function() {
	return JsonFx.Lang.get(""/Foo.jbst,localizationKey"");
}
	]
]);
// initialize template in the context of ""this""
(function() {
	/* initialization code block, executed only once as control is loaded */
this.generateValue = function() {
return new Date().toString();
};

this.myInitTime = this.generateValue();
}).call(MyApp.MyJbstControl);";

			var actual = new JbstCompiler().Compile("~/Foo.jbst", input);

			Assert.Equal(expected, actual);
		}

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_VariousNestedControls_RendersJbst()
		{
			var input =
@"<%@ Control Name=""NestedControls"" Language=""JavaScript"" %>

<ul>
<!-- declaratively embedding the same JBST as in the programmatic example -->
<!-- this calls the Example.myOtherJBST control once for each of the childList items -->
<jbst:control name=""Example.myOtherJBST"" data=""<%= this.data.childList %>"" />
</ul>

<!-- declaratively embedding a simple child control which uses the same data as the parent -->
<jbst:control name=""Example.myBasicControl"" />

<!-- declaratively embedding a child control that is a wrapper -->
<jbst:control name=""Example.myWrapperControl"">

<!-- this content is inserted inside the other JBST control -->
<a href=""<%= this.data.linkUrl %>""><%= this.data.linkLabel %></a>

</jbst:control>";

			var expected =
@"/*global JsonML */
var NestedControls = JsonML.BST([
	"""",
	"" "",
	[
		""ul"",
		"" "",
		""""/* declaratively embedding the same JBST as in the programmatic example */,
		"" "",
		""""/* this calls the Example.myOtherJBST control once for each of the childList items */,
		"" "",
		function() {
	return JsonML.BST(Example.myOtherJBST).dataBind(this.data.childList, this.index, this.count);
}
	],
	"" "",
	""""/* declaratively embedding a simple child control which uses the same data as the parent */,
	"" "",
	function() {
	return JsonML.BST(Example.myBasicControl).dataBind(this.data, this.index, this.count);
},
	""""/* declaratively embedding a child control that is a wrapper */,
	"" "",
	function() {
	return JsonML.BST(Example.myWrapperControl).dataBind(this.data, this.index, this.count, {
		$: 
			[
				"""",
				"" "",
				""""/* this content is inserted inside the other JBST control */,
				"" "",
				[
					""a"",
					{
						href: 
							function() {
	return this.data.linkUrl;
}
					},
					function() {
	return this.data.linkLabel;
}
				],
				"" ""
			]
	});
			}
]);";

			var actual = new JbstCompiler().Compile("~/Foo.jbst", input);

			Assert.Equal(expected, actual);
		}

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_WrapperControl_RendersJbst()
		{
			var input =
@"<%@ Control Name=""WrapperControl"" Language=""JavaScript"" %>

	<!-- declaratively define a simple wrapper -->
	<div class=""MyOnionSkinWrapper1"">
		<div class=""MyOnionSkinWrapper2"">

		<!-- this is where the outer JBST control's content is inserted -->
		<jbst:placeholder />

	</div>
</div>";

			var expected =
@"/*global JsonML */
var WrapperControl = JsonML.BST([
	"""",
	"" "",
	""""/* declaratively define a simple wrapper */,
	"" "",
	[
		""div"",
		{
			""class"" : ""MyOnionSkinWrapper1""
		},
		"" "",
		[
			""div"",
			{
				""class"" : ""MyOnionSkinWrapper2""
			},
			"" "",
			""""/* this is where the outer JBST control's content is inserted */,
			"" "",
			function() {
	var inline = ""$"",
		parts = this.args;

	if (parts && parts[inline]) {
		return JsonML.BST(parts[inline]).dataBind(this.data, this.index, this.count, parts);
	}
}
		],
		"" ""
	]
]);
";

			var actual = new JbstCompiler().Compile("~/Foo.jbst", input);

			Assert.Equal(expected, actual);
		}

		#endregion Foo Tests

		#region Input Edge Case Tests

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_EmptyInputText_RendersEmptyString()
		{
			var expected =
@"/*global JsonML */
var $_Foo_jbst = JsonML.BST(null);
";

			var input = "";

			var actual = new JbstCompiler().Compile("~/Foo.jbst", input);

			Assert.Equal(expected, actual);
		}

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_EmptyInputReader_RendersEmptyString()
		{
			var expected =
@"/*global JsonML */
var $_Foo_jbst = JsonML.BST(null);
";

			var actual = new StringBuilder();
			using (StringWriter writer = new StringWriter(actual))
			{
				new JbstCompiler().Compile("~/Foo.jbst", TextReader.Null, writer);
			}

			Assert.Equal(expected, actual.ToString());
		}

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_NullInputText_ThrowsArgumentNullException()
		{
			var input = (string)null;

			ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
				delegate
				{
					var actual = new JbstCompiler().Compile("~/Foo.jbst", input);
				});

			// verify exception is coming from expected param
			Assert.Equal("input", ex.ParamName);
		}

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_NullInputReader_ThrowsArgumentNullException()
		{
			var input = (TextReader)null;
			var output = TextWriter.Null;

			ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
				delegate
				{
					new JbstCompiler().Compile("~/Foo.jbst", input, output);
				});

			// verify exception is coming from expected param
			Assert.Equal("input", ex.ParamName);
		}

		[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_NullOutputWriter_ThrowsArgumentNullException()
		{
			var input = TextReader.Null;
			var output = (TextWriter)null;

			ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
				delegate
				{
					new JbstCompiler().Compile("~/Foo.jbst", input, output);
				});

			// verify exception is coming from expected param
			Assert.Equal("output", ex.ParamName);
		}

		#endregion Input Edge Case Tests
	}
}
