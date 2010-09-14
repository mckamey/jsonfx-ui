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
	internal abstract class ClientBlock
	{
		#region Methods

		public abstract void Format(List<Token<MarkupTokenType>> buffer, List<KeyValuePair<string, CodeObject>> replacements);

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

			string replace;
			using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
			{
				// emit script to late-bind an element
				writer.Write("JsonML.BST(");

				if (this.NameExpr is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.NameExpr));
				}
				else
				{
					replace = this.NameExpr as string;
				}
				writer.Write(replace);

				writer.Write(").replace(\"");

				if (this.ElemID is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.ElemID));
				}
				else
				{
					replace = this.ElemID as string;
				}
				writer.Write(replace);

				writer.Write("\",");

				if (this.DataExpr is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.DataExpr));
				}
				else
				{
					replace = this.DataExpr as string;
				}
				writer.Write(replace);

				writer.Write(",");
				if (this.IndexExpr is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.IndexExpr));
				}
				else
				{
					replace = this.IndexExpr as string;
				}
				writer.Write(replace);

				writer.Write(",");
				if (this.CountExpr is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.CountExpr));
				}
				else
				{
					replace = this.CountExpr as string;
				}
				writer.Write(replace);

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

			string replace;
			EcmaScriptFormatter jsFormatter = new EcmaScriptFormatter(new DataWriterSettings());
			using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
			{
				// emit script to set late-bound attributes
				// emit script to late-bind an element
				writer.Write("JsonML.BST(");

				//if (this.NameExpr is CodeObject)
				//{
				//    replace = Guid.NewGuid().ToString("B");
				//    replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.NameExpr));
				//}
				//else
				//{
				//    replace = this.NameExpr as string;
				//}
				//writer.Write(replace);

				writer.Write(").patch(\"");

				if (this.ElemID is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.ElemID));
				}
				else
				{
					replace = this.ElemID as string;
				}
				writer.Write(replace);

				writer.Write("\",");
				jsFormatter.Format(tokens, writer);

				writer.Write(",");
				if (this.DataExpr is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.DataExpr));
				}
				else
				{
					replace = this.DataExpr as string;
				}
				writer.Write(replace);

				writer.Write(",");
				if (this.IndexExpr is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.IndexExpr));
				}
				else
				{
					replace = this.IndexExpr as string;
				}
				writer.Write(replace);

				writer.Write(",");
				if (this.CountExpr is CodeObject)
				{
					replace = Guid.NewGuid().ToString("B");
					replacements.Add(new KeyValuePair<string, CodeObject>(replace, (CodeObject)this.CountExpr));
				}
				else
				{
					replace = this.CountExpr as string;
				}
				writer.Write(replace);

				writer.Write(");");

				// write script to output buffer
				buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, writer.ToString()));
			}

			buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));
		}

		#endregion Methods
	}
}
