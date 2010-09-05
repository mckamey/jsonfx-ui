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

using JsonFx.Markup;
using JsonFx.Serialization;
using Xunit;

using Assert=JsonFx.AssertPatched;

namespace JsonFx.Jbst
{
	public class JbstCompilerServerTests
	{
		#region Constants

		private const string TraitName = "JBST";
		private const string TraitValue = "Server Compiler";

		#endregion Constants

		#region Server-Side Tests

		//[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_MyZebraListServer_RendersJbst()
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
@"TODO";

			StringWriter writer = new StringWriter();
			new JbstCompiler().Compile("~/Foo.jbst", new StringReader(input), TextWriter.Null, writer);
			var actual = writer.GetStringBuilder().ToString();

			Assert.Equal(expected, actual);
		}

		//[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_MyJbstControlServer_RendersJbst()
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

			var expected = @"TODO";

			StringWriter writer = new StringWriter();
			new JbstCompiler("Blah").Compile("~/Foo.jbst", new StringReader(input), TextWriter.Null, writer);
			var actual = writer.GetStringBuilder().ToString();

			Assert.Equal(expected, actual);
		}

		//[Fact]
		[Trait(TraitName, TraitValue)]
		public void Compile_MyJbstControlServerVB_RendersJbst()
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

			var expected = @"TODO";

			StringWriter writer = new StringWriter();
			new JbstCompiler("Blah", new Microsoft.VisualBasic.VBCodeProvider()).Compile("~/Foo.jbst", new StringReader(input), TextWriter.Null, writer);
			var actual = writer.GetStringBuilder().ToString();

			Assert.Equal(expected, actual);
		}

		#endregion Server-Side Tests
	}
}
