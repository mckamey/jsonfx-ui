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

using JsonFx.Model;
using JsonFx.Serialization;
using JsonFx.Utils;

namespace JsonFx.Jbst.Extensions
{
	internal class ResourceJbstExtension : JbstExtension
	{
		#region Constants

		private static readonly char[] KeyDelim = { ',' };

		private const string ResourceLookupBegin =
@"function() {
	return JsonFx.Lang.get(";

		private const string ResourceLookupEnd =
	@");
}";

		#endregion Constants

		#region Fields

		private readonly string ClassKey;
		private readonly string ResourceKey;
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
			if (value == null)
			{
				value = String.Empty;
			}

			// split on first ','
			string[] parts = value.Split(ResourceJbstExtension.KeyDelim, 2, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length > 1)
			{
				this.ClassKey = parts[1].Trim();
			}

			this.ResourceKey = parts[0].Trim();
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
					this.globalizationKey = ResourceJbstExtension.GetKey(this.ClassKey, this.ResourceKey, this.Path);
				}
				return this.globalizationKey;
			}
		}

		#endregion Properties

		#region JbstExtension Members

		public override void Format(ITextFormatter<ModelTokenType> formatter, TextWriter writer)
		{
			if (this.ResourceKey == null)
			{
				base.Format(formatter, writer);
				return;
			}

			writer.Write(ResourceLookupBegin);

			// serialize the key to the writer
			formatter.Format(new [] { new Token<ModelTokenType>(ModelTokenType.Primitive, this.GlobalizationKey) }, writer);

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
		public static string GetKey(string classKey, string resourceKey, string path)
		{
			if (String.IsNullOrEmpty(classKey))
			{
				if (String.IsNullOrEmpty(path))
				{
					return resourceKey;
				}

				path = PathUtility.EnsureAppRelative(path).TrimStart('~');

				return path + ',' + resourceKey;
			}

			return classKey + ',' + resourceKey;
		}

		#endregion Utility Methods
	}
}
