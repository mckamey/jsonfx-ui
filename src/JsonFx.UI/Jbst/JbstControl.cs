﻿#region License
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

namespace JsonFx.Jbst
{
	public abstract class JbstControl
	{
		#region Delegates

		protected delegate void BindDelegate(TextWriter writer, object data, int index, int count);

		#endregion Delegates

		#region Constants

		private static readonly object DefaultData = new object();
		private const int DefaultIndex = 0;
		private const int DefaultCount = 1;

		#endregion Constants

		#region Bind Methods

		public void Bind(TextWriter writer)
		{
			this.Bind(writer, DefaultData, DefaultIndex, DefaultCount);
		}

		public void Bind(TextWriter writer, object data)
		{
			this.Bind(writer, data, DefaultIndex, DefaultCount);
		}

		public abstract void Bind(TextWriter writer, object data, int index, int count);

		protected void BindInternal(BindDelegate binder, TextWriter writer, object data, int index, int count)
		{
		}

		#endregion Bind Methods
	}
}