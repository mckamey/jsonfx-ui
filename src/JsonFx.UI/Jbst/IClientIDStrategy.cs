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

namespace JsonFx.Jbst
{
	/// <summary>
	/// A strategy for generating unique client IDs per request
	/// </summary>
	public interface IClientIDStrategy
	{
		string NextID();
	}

	/// <summary>
	/// Uses Guids to generate unique client IDs
	/// </summary>
	public class GuidClientIDStrategy : IClientIDStrategy
	{
		#region Fields

		private readonly string Prefix;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public GuidClientIDStrategy()
			: this("_")
		{
		}

		/// <summary>
		/// Ctor
		/// </summary>
		public GuidClientIDStrategy(string prefix)
		{
			this.Prefix = prefix;
		}

		#endregion Init

		#region IUniqueIDStrategy Members

		/// <summary>
		/// Gets the next ID
		/// </summary>
		/// <returns></returns>
		public string NextID()
		{
			return String.Concat(this.Prefix, Guid.NewGuid().ToString("n"));
		}

		#endregion IUniqueIDStrategy Members
	}

	/// <summary>
	/// Uses incrementing counter to generate unique client IDs
	/// </summary>
	public class IncClientIDStrategy : IClientIDStrategy
	{
		#region Fields

		private readonly string Prefix;
		private int counter = 0;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public IncClientIDStrategy()
			: this("T_")
		{
		}

		/// <summary>
		/// Ctor
		/// </summary>
		public IncClientIDStrategy(string prefix)
		{
			this.Prefix = prefix;
		}

		#endregion Init

		#region IUniqueIDStrategy Members

		/// <summary>
		/// Gets the next ID
		/// </summary>
		/// <returns></returns>
		public string NextID()
		{
			return String.Concat(this.Prefix, (this.counter++).ToString("0000"));
		}

		#endregion IUniqueIDStrategy Members
	}
}
