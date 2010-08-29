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
using System.Diagnostics;

using JsonFx.Serialization;
using Microsoft.Ajax.Utilities;

namespace JsonFx.EcmaScript
{
	internal class TranslationResult
	{
		#region Fields

		public readonly Type ResultType;
		public readonly CodeMemberMethod Method = new CodeMemberMethod();

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="resultType"></param>
		/// <param name="methodName"></param>
		public TranslationResult(Type resultType, string methodName)
		{
			this.ResultType = resultType;

			this.Method = new CodeMemberMethod
				{
					Name = methodName
				};

			this.Method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(object), "data"));
			this.Method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "index"));
			this.Method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "count"));
		}

		#endregion Init

		#region Properties

		public bool IsClientOnly { get; set; }

		public string Script { get; set; }

		public int LineCount
		{
			get { return this.Method.Statements.Count; }
		}

		#endregion Properties

		#region Methods

		/// <summary>
		/// Adds the statement or expression to the method being built.
		/// </summary>
		/// <param name="code"></param>
		/// <returns>true if recognized, false if type was not supported</returns>
		public bool AddStatement(ExpressionResult expr)
		{
			if (expr == null)
			{
				return false;
			}

			if (expr.Expression != null)
			{
				this.Method.Statements.Add(expr.Expression);
				return true;
			}

			if (expr.Statement != null)
			{
				this.Method.Statements.Add(expr.Statement);
				return true;
			}

			return false;
		}

		public void TranslationComplete()
		{
			var index = this.Method.Statements.Count-1;
			if (index < 0)
			{
				return;
			}

			// extract expression
			CodeExpression expr;
			if (this.Method.Statements[index] is CodeExpressionStatement)
			{
				CodeExpressionStatement statement = (CodeExpressionStatement)this.Method.Statements[index];
				expr = statement.Expression;
			}
			else if (this.Method.Statements[index] is CodeMethodReturnStatement)
			{
				CodeMethodReturnStatement statement = (CodeMethodReturnStatement)this.Method.Statements[index];
				expr = statement.Expression;
			}
			else
			{
				// bail.
				return;
			}

			bool needsCoercion = (this.ResultType != typeof(object));
			if (needsCoercion)
			{
				if (expr is CodeArgumentReferenceExpression)
				{
					string paramName = ((CodeArgumentReferenceExpression)expr).ParameterName;
					if ((paramName == "index") || (paramName == "count"))
					{
						if (this.ResultType.IsAssignableFrom(typeof(int)))
						{
							needsCoercion = false;
						}
					}
				}
				else if (expr is CodePrimitiveExpression)
				{
					object value = ((CodePrimitiveExpression)expr).Value;
					Type primitiveType = (value != null) ? value.GetType() : typeof(object);
					if (this.ResultType.IsAssignableFrom(primitiveType))
					{
						needsCoercion = false;
					}
				}

				if (needsCoercion)
				{
					expr = this.WrapWithCoercion(this.ResultType, expr);
				}
			}

			// convert expression statement to return statement
			this.Method.Statements[index] = new CodeMethodReturnStatement(expr);
			this.Method.ReturnType = new CodeTypeReference(this.ResultType);
		}

		private CodeExpression WrapWithCoercion(Type type, CodeExpression expr)
		{
			// wrap expression in a coercion call before return
			return new CodeMethodInvokeExpression(
				new CodeMethodReferenceExpression(
					new CodeThisReferenceExpression(),
					"CoerceType",
					new CodeTypeReference(type)),
				expr);
		}

		#endregion Methods
	}
}
