﻿using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;

namespace Kurogane {

	public class Scope : IDynamicMetaObjectProvider {

		private readonly object key = new object();
		private readonly IDictionary<string, dynamic> _values = new Dictionary<string, dynamic>();
		private readonly Scope _parent;

		public Scope() {
			_parent = null;
		}

		public Scope(Scope parent) {
			_parent = parent;
		}

		public bool HasVariable(string name) {
			lock (key) {
				if (_values.ContainsKey(name))
					return true;
			}
			return _parent != null && _parent.HasVariable(name);
		}

		public object GetVariable(string name) {
			lock (key) {
				object value = null;
				if (_values.TryGetValue(name, out value))
					return value;
			}
			if (_parent == null)
				throw new VariableNotFoundException(name);
			else
				return _parent.GetVariable(name);
		}

		public object SetVariable(string name, object value) {
			lock (key) {
				return _values[name] = value;
			}
		}

		#region IDynamicMetaObjectProvider

		public DynamicMetaObject GetMetaObject(Expression parameter) {
			return new ScopeMetaObject(this, parameter);
		}

		internal class ScopeMetaObject : DynamicMetaObject {

			public ScopeMetaObject(Scope self, Expression expr)
				: base(expr, BindingRestrictions.GetExpressionRestriction(Expression.TypeIs(expr, typeof(Scope))), self) {
			}

			public override IEnumerable<string> GetDynamicMemberNames() {
				return ((Scope)base.Value)._values.Keys;
			}

			public override DynamicMetaObject BindGetMember(GetMemberBinder binder) {
				return new DynamicMetaObject(
					Expression.Call(
						Expression.Convert(this.Expression, typeof(Scope)),
						typeof(Scope).GetMethod("GetVariable"),
						Expression.Constant(binder.Name)),
					this.Restrictions);
			}

			public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value) {
				return new DynamicMetaObject(
					Expression.Call(
						Expression.Convert(this.Expression, typeof(Scope)),
						typeof(Scope).GetMethod("SetVariable"),
						Expression.Constant(binder.Name),
						Expression.Convert(value.Expression, typeof(object))),
					this.Restrictions);
			}
		}

		#endregion

	}
}
