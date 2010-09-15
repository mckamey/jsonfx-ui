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
using System.CodeDom;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using JsonFx.EcmaScript;
using JsonFx.Markup;
using JsonFx.Model;
using JsonFx.Serialization;

namespace JsonFx.Jbst
{
	internal class ClientDeferredCode : CodeObject
	{
		#region Fields

		public readonly string Script;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="script"></param>
		public ClientDeferredCode(string script)
		{
			this.Script = script;
		}

		#endregion Init
	}

	internal abstract class ClientBlock
	{
		#region Fields

		private DataWriterSettings settings;
		private ModelWalker walker;
		private EcmaScriptFormatter formatter;

		#endregion Fields

		#region Methods

		public abstract void Format(List<Token<MarkupTokenType>> buffer, List<KeyValuePair<string, CodeObject>> replacements);

		protected void Format(object value, TextWriter writer, List<KeyValuePair<string, CodeObject>> replacements)
		{
			if (value is CodeObject)
			{
				string replace = Guid.NewGuid().ToString("B");
				replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)value));
				writer.Write(replace);
				return;
			}
			
			if (value is JbstCommand)
			{
				this.Format(value, writer);
				return;
			}

			writer.Write(value);
		}

		private void Format(object value, TextWriter writer)
		{
			if (this.settings == null)
			{
				this.settings = new DataWriterSettings();
			}
			if (this.walker == null)
			{
				this.walker = new ModelWalker(this.settings);
			}

			var tokens = (value is ITextFormattable<ModelTokenType>) ?
				new[] { new Token<ModelTokenType>(ModelTokenType.Primitive, value) }:
				this.walker.GetTokens(value);

			this.Format(tokens, writer);
		}

		protected void Format(IEnumerable<Token<ModelTokenType>> tokens, TextWriter writer)
		{
			if (this.settings == null)
			{
				this.settings = new DataWriterSettings();
			}
			if (this.formatter == null)
			{
				this.formatter = new EcmaScriptFormatter(this.settings);
			}

			this.formatter.Format(tokens, writer);
		}

		#endregion Methods
	}

	internal class ReplaceClientBlock : ClientBlock
	{
		#region Fields

		private readonly object ElemID;
		private readonly object NameExpr;
		private readonly object DataExpr;
		private readonly object IndexExpr;
		private readonly object CountExpr;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="attrs"></param>
		public ReplaceClientBlock(object elemID, object nameExpr, object dataExpr, object indexExpr, object countExpr)
		{
			this.ElemID = elemID;
			this.NameExpr = nameExpr;
			this.DataExpr = dataExpr;
			this.IndexExpr = indexExpr;
			this.CountExpr = countExpr;
		}

		#endregion Init

		#region Methods

		public override void Format(List<Token<MarkupTokenType>> buffer, List<KeyValuePair<string, CodeObject>> replacements)
		{
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, new DataName("script")));
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, new DataName("type")));
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, "text/javascript"));

			using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
			{
				// emit script to late-bind an element
				writer.Write("JsonML.BST(");

				this.Format(this.NameExpr, writer, replacements);
				writer.Write(").replace(\"");

				this.Format(this.ElemID, writer, replacements);
				writer.Write("\",");

				this.Format(this.DataExpr, writer, replacements);
				writer.Write(",");

				this.Format(this.IndexExpr, writer, replacements);
				writer.Write(",");

				this.Format(this.CountExpr, writer, replacements);
				writer.Write(");");

				// write script to output buffer
				buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, writer.ToString()));
			}

			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));
		}

		#endregion Methods
	}

	internal class DisplaceClientBlock : ClientBlock
	{
		#region Fields

		private readonly object ElemID;
		//private readonly object NameExpr;
		private readonly object DataExpr;
		private readonly object IndexExpr;
		private readonly object CountExpr;
		private readonly string Script;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="attrs"></param>
		public DisplaceClientBlock(object elemID, /*object nameExpr,*/ object dataExpr, object indexExpr, object countExpr, string script)
		{
			this.ElemID = elemID;
			//this.NameExpr = nameExpr;
			this.DataExpr = dataExpr;
			this.IndexExpr = indexExpr;
			this.CountExpr = countExpr;
			this.Script = script;
		}

		#endregion Init

		#region Methods

		public override void Format(List<Token<MarkupTokenType>> buffer, List<KeyValuePair<string, CodeObject>> replacements)
		{
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, new DataName("script")));
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, new DataName("type")));
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, "text/javascript"));

			using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
			{
				// emit script to set late-bound attributes
				// emit script to late-bind an element
				writer.Write("JsonML.BST(");

				//this.Emit(this.NameExpr, writer, replacements);

				writer.Write(").displace(\"");

				this.Format(this.ElemID, writer, replacements);
				writer.Write("\",");

				writer.Write(this.Script);
				writer.Write(",");

				this.Format(this.DataExpr, writer, replacements);
				writer.Write(",");

				this.Format(this.IndexExpr, writer, replacements);
				writer.Write(",");

				this.Format(this.CountExpr, writer, replacements);
				writer.Write(");");

				// write script to output buffer
				buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, writer.ToString()));
			}

			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));
		}

		#endregion Methods
	}

	internal class PatchClientBlock : ClientBlock
	{
		#region Fields

		private readonly object ElemID;
		private readonly object NameExpr;
		private readonly object DataExpr;
		private readonly object IndexExpr;
		private readonly object CountExpr;
		private readonly IDictionary<DataName, object> Attrs;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="attrs"></param>
		public PatchClientBlock(object elemID, /*object nameExpr,*/ object dataExpr, object indexExpr, object countExpr, IDictionary<DataName, object> attrs)
		{
			this.ElemID = elemID;
			//this.NameExpr = nameExpr;
			this.DataExpr = dataExpr;
			this.IndexExpr = indexExpr;
			this.CountExpr = countExpr;
			this.Attrs = attrs;
		}

		#endregion Init

		#region Methods

		public override void Format(List<Token<MarkupTokenType>> buffer, List<KeyValuePair<string, CodeObject>> replacements)
		{
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, new DataName("script")));
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, new DataName("type")));
			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, "text/javascript"));

			List<Token<ModelTokenType>> tokens = new List<Token<ModelTokenType>>(this.Attrs.Count * 2 + 2);
			tokens.Add(new Token<ModelTokenType>(ModelTokenType.ObjectBegin));
			foreach (var pair in this.Attrs)
			{
				tokens.Add(new Token<ModelTokenType>(ModelTokenType.Property, pair.Key));
				tokens.Add(new Token<ModelTokenType>(ModelTokenType.Primitive, pair.Value));
			}
			tokens.Add(new Token<ModelTokenType>(ModelTokenType.ObjectEnd));

			using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
			{
				// emit script to set late-bound attributes
				// emit script to late-bind an element
				writer.Write("JsonML.BST(");

				//this.Format(this.NameExpr, writer, replacements);
				writer.Write(").patch(\"");

				this.Format(this.ElemID, writer, replacements);
				writer.Write("\",");

				this.Format(tokens, writer);
				writer.Write(",");

				this.Format(this.DataExpr, writer, replacements);
				writer.Write(",");

				this.Format(this.IndexExpr, writer, replacements);
				writer.Write(",");

				this.Format(this.CountExpr, writer, replacements);
				writer.Write(");");

				// write script to output buffer
				buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, writer.ToString()));
			}

			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));
		}

		#endregion Methods
	}
}
