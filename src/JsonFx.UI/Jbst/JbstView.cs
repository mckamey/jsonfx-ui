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
using System.Collections;
using System.Collections.Generic;
using System.IO;

using JsonFx.Model;
using JsonFx.Serialization;

using TokenSequence=System.Collections.Generic.IEnumerable<JsonFx.Serialization.Token<JsonFx.Model.ModelTokenType>>;

namespace JsonFx.Jbst
{
	public abstract class JbstView
	{
		#region Delegates

		protected delegate void BindDelegate(TextWriter writer, object data, int index, int count);

		#endregion Delegates

		#region Constants

		private static readonly object DefaultData = new object();
		private const int DefaultIndex = 0;
		private const int DefaultCount = 1;

		#endregion Constants

		#region Fields

		protected readonly DataWriterSettings Settings;
		private readonly TypeCoercionUtility Coercion;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		protected JbstView()
			: this(new DataWriterSettings())
		{
		}

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		protected JbstView(DataWriterSettings settings)
		{
			this.Settings = settings;
			this.Coercion = new TypeCoercionUtility(settings, true);
		}

		#endregion Init

		#region Bind Methods

		/// <summary>
		/// Binds the JBST without any data to the given writer
		/// </summary>
		/// <param name="writer"></param>
		public void Bind(TextWriter writer)
		{
			this.Bind(writer, DefaultData, DefaultIndex, DefaultCount);
		}

		/// <summary>
		/// Binds the JBST to the given data to the given writer
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="data"></param>
		public void Bind(TextWriter writer, object data)
		{
			this.Bind(writer, data, DefaultIndex, DefaultCount);
		}

		/// <summary>
		/// Binds the JBST to the given data to the given writer
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="data"></param>
		/// <param name="index"></param>
		/// <param name="count"></param>
		public void Bind(TextWriter writer, object data, int index, int count)
		{
			this.Bind(this.Root, writer, data, index, count, true);
		}

		protected internal abstract void Root(TextWriter writer, object data, int index, int count);

		#endregion Bind Methods

		#region Supporting Methods

		/// <summary>
		/// Binds an external JBST view as a child template
		/// </summary>
		/// <param name="view"></param>
		/// <param name="writer"></param>
		/// <param name="data"></param>
		/// <param name="index"></param>
		/// <param name="count"></param>
		protected void Bind(JbstView view, TextWriter writer, object data, int index, int count)
		{
			// skips the normalization step
			this.Bind(view.Root, writer, data, index, count, false);
		}

		/// <summary>
		/// Implements the logic for deconstructing into token sequences and looping if input is an array
		/// </summary>
		/// <param name="binder"></param>
		/// <param name="writer"></param>
		/// <param name="data"></param>
		/// <param name="index"></param>
		/// <param name="count"></param>
		protected void Bind(BindDelegate binder, TextWriter writer, object data, int index, int count, bool normalize)
		{
			if (normalize)
			{
				var sequence = data as TokenSequence;
				if (sequence == null)
				{
					// serialize to tokens
					sequence = new ModelWalker(this.Settings).GetTokens(data);
				}

				// hydrate back to normalized objects
				DataReaderSettings settings = new DataReaderSettings(this.Settings.Resolver, this.Settings.Filters);

				// FirstOrDefault
				data = null;
				foreach (var result in new ModelAnalyzer(settings).Analyze(sequence))
				{
					data = result;
					break;
				}
			}

			IList array = data as IList;
			if (array != null)
			{
				for (int i=0, length=array.Count; i<length; i++)
				{
					binder(writer, array[i], i, length);
				}
			}
			else
			{
				binder(writer, data, index, count);
			}
		}

		/// <summary>
		/// CodeGen glue
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="value"></param>
		/// <returns></returns>
		protected T CoerceType<T>(object value)
		{
			return this.Coercion.CoerceType<T>(value);
		}

		/// <summary>
		/// CodeGen glue
		/// </summary>
		/// <param name="input"></param>
		/// <param name="propertyName"></param>
		/// <returns></returns>
		protected object GetProperty(object input, string propertyName)
		{
			// normalized objects are always IDictionary<string, object>
			IDictionary<string, object> genericDictionary = input as IDictionary<string, object>;
			if (genericDictionary != null)
			{
				object value;
				if (genericDictionary.TryGetValue(propertyName, out value))
				{
					return value;
				}
				return null;
			}

			// normalized arrays are always IList
			IList list = input as IList;
			if (list != null)
			{
				if (propertyName == "length")
				{
					return list.Count;
				}

				// try propertyName as an index
				int index;
				if (Int32.TryParse(propertyName, out index))
				{
					return list[index];
				}

				// Arrays do not have other property names
				return null;
			}

			// primitives do not have properties?
			return null;
		}

		#endregion Supporting Methods
	}
}
