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
using System.Collections.Generic;

using JsonFx.Markup;

namespace JsonFx.Jbst
{
	internal class CompilationState
	{
		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="path"></param>
		public CompilationState(string path)
		{
			this.Path = path;
		}

		#endregion Init

		#region Fields

		public readonly string Path;
		public readonly List<string> Imports = new List<string>();
		public readonly JbstDeclarationBlock Declarations = new JbstDeclarationBlock();

		#endregion Fields

		#region Properties

		public string JbstName
		{
			get;
			set;
		}

		public AutoMarkupType AutoMarkup
		{
			get;
			set;
		}

		#endregion Properties
	}
}