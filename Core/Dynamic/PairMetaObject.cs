﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Dynamic;
using System.Linq.Expressions;

namespace Kurogane.Dynamic {

	class PairMetaObject : DynamicMetaObject {

		public PairMetaObject(IPair pair, Expression expr)
			: base(expr, BindingRestrictions.Empty, pair) {
		}

		public override DynamicMetaObject BindGetMember(GetMemberBinder binder) {
			string propName = null;
			switch (binder.Name) {
			case "頭": propName = "Head"; break;
			case "体": propName = "Tail"; break;
			}
			if (propName != null)
				return new DynamicMetaObject(
					Expression.Property(Expression.Convert(base.Expression, typeof(IPair)), propName),
					BindingRestrictions.GetExpressionRestriction(Expression.TypeIs(base.Expression, typeof(IPair))));
			return base.BindGetMember(binder);
		}
	}
}