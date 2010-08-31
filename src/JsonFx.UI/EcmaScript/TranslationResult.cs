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

namespace JsonFx.EcmaScript
{
	internal class TranslationResult
	{
		#region Fields

		public readonly Type ResultType;
		public readonly IList<CodeMemberMethod> Methods = new List<CodeMemberMethod>();

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

			var method = new CodeMemberMethod();

			method.Name = methodName;

			// TODO: pass parameter list as property on TranslationResult
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(object), "data"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "index"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "count"));

			this.Methods.Add(method);
		}

		#endregion Init

		#region Properties

		public bool IsClientOnly { get; set; }

		public string Script { get; set; }

		/// <summary>
		/// Gets a quick view of the complexity of the result
		/// </summary>
		public int LineCount
		{
			get
			{
				int count = 0;
				foreach (var method in this.Methods)
				{
					count += method.Statements.Count;
				}
				return count;
			}
		}

		#endregion Properties

		#region Methods

		/// <summary>
		/// Adds the statement or expression to the method being built.
		/// </summary>
		/// <param name="code"></param>
		/// <returns>true if recognized, false if type was not supported</returns>
		public bool AddResult(ExpressionResult expr)
		{
			if (expr == null)
			{
				return false;
			}

			bool valid = false;

			if (expr.Expression != null)
			{
				this.Methods[0].Statements.Add(expr.Expression);
				valid = true;
			}
			else if (expr.Statement != null)
			{
				this.Methods[0].Statements.Add(expr.Statement);
				valid = true;
			}
			
			if (expr.Method != null)
			{
				this.Methods.Add(expr.Method);
				valid = true;
			}

			return valid;
		}

		/// <summary>
		/// Ensures return type is correct and set if needed.
		/// </summary>
		public void EnsureReturnType(bool addReturn)
		{
			if (this.ResultType == typeof(object))
			{
				// no coercion needed
				return;
			}

			bool hasReturn = false;
			for (int i=0, length=this.Methods[0].Statements.Count; i<length; i++)
			{
				CodeMethodReturnStatement statement;

				if (addReturn && (i+1 == length))
				{
					CodeExpressionStatement last = this.Methods[0].Statements[i] as CodeExpressionStatement;
					if (last == null)
					{
						continue;
					}

					// convert last expression to a return statement
					this.Methods[0].Statements[i]  = statement = new CodeMethodReturnStatement(last.Expression);
				}
				else
				{
					statement = this.Methods[0].Statements[i] as CodeMethodReturnStatement;
				}
				if (statement == null)
				{
					continue;
				}

				hasReturn = true;
				bool needsCoercion = true;

				// extract expression
				CodeExpression expr = statement.Expression;
				if (expr is CodeArgumentReferenceExpression)
				{
					// TODO: pass parameter list as property on TranslationResult
					string paramName = ((CodeArgumentReferenceExpression)expr).ParameterName;
					if (((paramName == "index") || (paramName == "count")) &&
						this.ResultType.IsAssignableFrom(typeof(int)))
					{
						needsCoercion = false;
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

				if (!needsCoercion)
				{
					continue;
				}

				// wrap expression with type coercion call
				statement.Expression = new CodeMethodInvokeExpression(
					new CodeMethodReferenceExpression(
						new CodeThisReferenceExpression(),
						"CoerceType",
						new CodeTypeReference(this.ResultType)),
						expr);
			}

			if (hasReturn)
			{
				// only set return type if actually had a return statement
				this.Methods[0].ReturnType = new CodeTypeReference(this.ResultType);
			}
		}

		#endregion Methods
	}
}
