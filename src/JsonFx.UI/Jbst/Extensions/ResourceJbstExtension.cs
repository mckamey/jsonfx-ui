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
using System.Web.Compilation;

using JsonFx.Common;
using JsonFx.Serialization;
using JsonFx.Utils;

namespace JsonFx.Jbst.Extensions
{
	internal class ResourceJbstExtension : JbstExtension
	{
		#region Constants

		private const string ResourceLookupBegin =
@"function() {
	return JsonFx.Lang.get(";

		private const string ResourceLookupEnd =
	@");
}";

		#endregion Constants

		#region Fields

		private readonly ResourceExpressionFields ResKey;
		private string globalizationKey = null;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="value"></param>
		/// <param name="path"></param>
		protected internal ResourceJbstExtension(string value, string path)
			: base(value, path)
		{
			this.ResKey = ResourceExpressionBuilder.ParseExpression(this.Value);
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets the resource key for this expression
		/// </summary>
		public string GlobalizationKey
		{
			get
			{
				if (this.globalizationKey == null)
				{
					this.globalizationKey = ResourceJbstExtension.GetKey(this.ResKey, this.Path);
				}
				return this.globalizationKey;
			}
		}

		#endregion Properties

		#region JbstExtension Members

		public override void Format(ITextFormatter<CommonTokenType> formatter, TextWriter writer)
		{
			if (this.ResKey == null)
			{
				base.Format(formatter, writer);
				return;
			}

			writer.Write(ResourceLookupBegin);

			// serialize the key to the writer
			formatter.Format(new [] { new Token<CommonTokenType>(CommonTokenType.Primitive, this.GlobalizationKey) }, writer);

			writer.Write(ResourceLookupEnd);
		}

		#endregion JbstExtension Members

		#region Utility Methods

		/// <summary>
		/// Resolves the resource key
		/// </summary>
		/// <param name="fields"></param>
		/// <param name="path"></param>
		/// <returns></returns>
		public static string GetKey(ResourceExpressionFields fields, string path)
		{
			if (fields == null)
			{
				return String.Empty;
			}
			if (String.IsNullOrEmpty(fields.ClassKey))
			{
				if (String.IsNullOrEmpty(path))
				{
					return fields.ResourceKey;
				}

				path = PathUtility.EnsureAppRelative(path).TrimStart('~');

				return path + ',' + fields.ResourceKey;
			}

			return fields.ClassKey + ',' + fields.ResourceKey;
		}

		#endregion Utility Methods
	}
}
