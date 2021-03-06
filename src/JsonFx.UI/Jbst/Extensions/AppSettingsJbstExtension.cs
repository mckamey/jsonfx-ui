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
using System.Configuration;
using System.IO;

using JsonFx.Model;
using JsonFx.Serialization;

namespace JsonFx.Jbst.Extensions
{
	internal class AppSettingsJbstExtension : JbstExtension
	{
		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="value"></param>
		/// <param name="path"></param>
		protected internal AppSettingsJbstExtension(string value, string path)
			: base(value, path)
		{
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// Gets the command type
		/// </summary>
		public override JbstCommandType CommandType
		{
			get { return JbstCommandType.AppSettingsExtension; }
		}

		#endregion Properties

		#region JbstExtension Members

		public override void Format(ITextFormatter<ModelTokenType> formatter, TextWriter writer)
		{
			string appSettingsKey = this.Value.Trim();

			if (String.IsNullOrEmpty(appSettingsKey))
			{
				base.Format(formatter, writer);
				return;
			}

			formatter.Format(new[] { new Token<ModelTokenType>(ModelTokenType.Primitive, ConfigurationManager.AppSettings[appSettingsKey]) });
		}

		#endregion JbstExtension Members
	}
}
