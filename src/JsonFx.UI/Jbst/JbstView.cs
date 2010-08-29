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
		private static readonly TokenSequence EmptySequence = new Token<ModelTokenType>[0];
		private const int DefaultIndex = 0;
		private const int DefaultCount = 1;

		#endregion Constants

		#region Fields

		protected readonly DataWriterSettings Settings;
		private readonly ModelWalker Walker;
		private readonly ModelAnalyzer Analyzer;
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
			this.Walker = new ModelWalker(settings);
			this.Analyzer = new ModelAnalyzer(new DataReaderSettings(settings.Resolver, settings.Filters));
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
		public abstract void Bind(TextWriter writer, object data, int index, int count);

		#endregion Bind Methods

		#region Supporting Methods

		/// <summary>
		/// Implements the logic for deconstructing into token sequences and looping if input is an array
		/// </summary>
		/// <param name="binder"></param>
		/// <param name="writer"></param>
		/// <param name="data"></param>
		/// <param name="index"></param>
		/// <param name="count"></param>
		protected void Bind(BindDelegate binder, TextWriter writer, object data, int index, int count)
		{
			// TODO: refactor Bind so object normalization only occurs at top-level binding

			var sequence = (data ?? JbstView.EmptySequence) as TokenSequence;
			if (sequence == null)
			{
				// serialize to tokens
				sequence = this.Walker.GetTokens(data);
			}

			// check array at token level
			bool isArray = sequence.IsArray();

			// hydrate back to generalized objects
			data = null;
			foreach (var result in this.Analyzer.Analyze(sequence))
			{
				data = result;
				break;
			}

			if (!isArray)
			{
				binder(writer, data, index, count);
				return;
			}

			ICollection array = (data as ICollection);

			index = 0;
			count = array.Count;
			foreach (var item in array)
			{
				binder(writer, item, index++, count);
			}
		}

		/// <summary>
		/// Reconstructs token sub-sequences if needed and writes to output.
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="value"></param>
		protected void Write(TextWriter writer, object value)
		{
			//if (value is TokenSequence)
			//{
			//    // FirstOrDefault()
			//    foreach (var result in this.Analyzer.Analyze((TokenSequence)value))
			//    {
			//        writer.Write(result);
			//        break;
			//    }
			//}
			//else
			{
				writer.Write(value);
			}
		}

		/// <summary>
		/// TypeCoercionUtility CodeGen glue
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="value"></param>
		/// <returns></returns>
		protected T CoerceType<T>(object value)
		{
			//if (value is TokenSequence)
			//{
			//    if (typeof(TokenSequence).IsAssignableFrom(typeof(T)))
			//    {
			//        return this.Coercion.CoerceType<T>(value);
			//    }

			//    // FirstOrDefault()
			//    foreach (var result in this.Analyzer.Analyze<T>((TokenSequence)value))
			//    {
			//        return result;
			//    }
			//    return default(T);
			//}
			//else if (typeof(T).IsAssignableFrom(typeof(TokenSequence)))
			//{
			//    return (T)this.Walker.GetTokens(value);
			//}

			return this.Coercion.CoerceType<T>(value);
		}

		/// <summary>
		/// ModelSubsequencer CodeGen glue
		/// </summary>
		/// <param name="input"></param>
		/// <param name="propertyName"></param>
		/// <returns></returns>
		protected object GetProperty(object input, string propertyName)
		{
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

			IDictionary dictionary = input as IDictionary;
			if (dictionary != null)
			{
				return dictionary[propertyName];
			}

			IEnumerable enumerable = input as IEnumerable;
			if (enumerable != null)
			{
				if (propertyName == "length")
				{
					ICollection collection = input as ICollection;
					if (collection != null)
					{
						return collection.Count;
					}

					// count array length
					int count = 0;
					var enumerator = enumerable.GetEnumerator();
					while (enumerator.MoveNext())
					{
						count++;
					}
					return count;
				}

				int index;
				if (Int32.TryParse(propertyName, out index))
				{
					IList list = input as IList;
					if (list != null)
					{
						return list[index];
					}

					// get array item at index
					foreach (var item in enumerable)
					{
						index--;
						if (index < 0)
						{
							return item;
						}
					}
					return null;
				}

				// hmm...
				return null;
			}

			return null;
		}

		#endregion Supporting Methods
	}
}
