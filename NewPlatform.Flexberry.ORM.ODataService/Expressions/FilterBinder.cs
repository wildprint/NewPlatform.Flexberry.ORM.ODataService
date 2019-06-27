﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.
// Branch of https://github.com/OData/WebApi/blob/v5.7.0/OData/src/System.Web.OData/OData/Query/Expressions/FilterBinder.cs

namespace NewPlatform.Flexberry.ORM.ODataService.Expressions
{
    using Microsoft.Spatial;
    using System;
    using System.Collections.Generic;
    //using System.Linq;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Web.Http;
    //using Microsoft.AspNettp.Dispatcher;
     using Microsoft.AspNet.OData.Formatter;
    //using Microsoft.AspNet.OData.Properties;
    using Microsoft.AspNet.OData.Query;
    using System.Xml.Linq;
    //using Microsoft.OData.Core;
    //using Microsoft.OData.Core.UriParser.Semantic;
    //using Microsoft.OData.Core.UriParser.TreeNodeKinds;
    //using Microsoft.OData.Edm;
    //using Microsoft.Data.OData.Query;
    //using Microsoft.OData.UriParser;
    //using Microsoft.Data.OData.Query.SemanticAst;
    //using QueryNode = Microsoft.OData.UriParser.QueryNode;
    //using CollectionNode = Microsoft.OData.UriParser.CollectionNode;
    using NewPlatform.Flexberry.ORM.ODataService.Core.Expressions;
    using Microsoft.OData.UriParser;
    using Microsoft.OData.Edm;
    using Microsoft.OData;

    // using Microsoft.Data.Edm;
    //using Microsoft.Data.OData;
    //using Microsoft.OData;

    // using Microsoft.OData;
    //using SingleValueNode = Microsoft.OData.UriParser.SingleValueNode;
    //using SingleValueFunctionCallNode = Microsoft.OData.UriParser.SingleValueFunctionCallNode;
    //using SingleValueOpenPropertyAccessNode = Microsoft.OData.UriParser.SingleValueOpenPropertyAccessNode;
    //using UnaryOperatorNode = Microsoft.OData.UriParser.UnaryOperatorNode;
    //using SingleNavigationNode = Microsoft.OData.UriParser.SingleNavigationNode;
    //using SingleValuePropertyAccessNode = Microsoft.OData.UriParser.SingleValuePropertyAccessNode;
    //using ConstantNode = Microsoft.OData.UriParser.ConstantNode;
    //using QueryNodeKind = Microsoft.OData.UriParser.QueryNodeKind;
    //using BinaryOperatorNode = Microsoft.OData.UriParser.BinaryOperatorNode;
    //using ConvertNode = Microsoft.OData.UriParser.ConvertNode;
    //using AnyNode = Microsoft.OData.UriParser.AnyNode;
    //using AllNode = Microsoft.OData.UriParser.AllNode;

    //using Microsoft.OData.Edm.Library;

    /// <summary>
    /// Translates an OData $filter parse tree represented by <see cref="FilterClause"/> to
    /// an <see cref="Expression"/> and applies it to an <see cref="IQueryable"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "Relies on many ODataLib classes.")]
    internal class FilterBinder
    {
        public List<string> FilterDetailProperties = new List<string>();
        /// <summary>
        /// Выражение linq, после преобразования из $filter.
        /// </summary>
        public LambdaExpression LinqExpression { get; private set; }

        /// <summary>
        /// Список типов, которые были параметрами в функции isof.
        /// </summary>
        public List<string> IsOfTypesList { get; private set; }

        private const string ODataItParameterName = "$it";

        private static readonly string _dictionaryStringObjectIndexerName = typeof(Dictionary<string, object>).GetDefaultMembers()[0].Name;

        private static readonly MethodInfo _stringCompareMethodInfo = typeof(string).GetMethod("Compare", new[] { typeof(string), typeof(string), typeof(StringComparison) });

        private static readonly Expression _nullConstant = Expression.Constant(null);
        private static readonly Expression _falseConstant = Expression.Constant(false);
        private static readonly Expression _trueConstant = Expression.Constant(true);
        private static readonly Expression _zeroConstant = Expression.Constant(0);
        private static readonly Expression _ordinalStringComparisonConstant = Expression.Constant(StringComparison.Ordinal);

        private static readonly MethodInfo _enumTryParseMethod = typeof(Enum).GetMethods()
                        .Single(m => m.Name == "TryParse" && m.GetParameters().Length == 2);

        private static Dictionary<Microsoft.OData.UriParser.BinaryOperatorKind, ExpressionType> _binaryOperatorMapping = new Dictionary<Microsoft.OData.UriParser.BinaryOperatorKind, ExpressionType>
        {
            { Microsoft.OData.UriParser.BinaryOperatorKind.Add, ExpressionType.Add },
            { Microsoft.OData.UriParser.BinaryOperatorKind.And, ExpressionType.AndAlso },
            { Microsoft.OData.UriParser.BinaryOperatorKind.Divide, ExpressionType.Divide },
            { Microsoft.OData.UriParser.BinaryOperatorKind.Equal, ExpressionType.Equal },
            { Microsoft.OData.UriParser.BinaryOperatorKind.GreaterThan, ExpressionType.GreaterThan },
            { Microsoft.OData.UriParser.BinaryOperatorKind.GreaterThanOrEqual, ExpressionType.GreaterThanOrEqual },
            { Microsoft.OData.UriParser.BinaryOperatorKind.LessThan, ExpressionType.LessThan },
            { Microsoft.OData.UriParser.BinaryOperatorKind.LessThanOrEqual, ExpressionType.LessThanOrEqual },
            { Microsoft.OData.UriParser.BinaryOperatorKind.Modulo, ExpressionType.Modulo },
            { Microsoft.OData.UriParser.BinaryOperatorKind.Multiply, ExpressionType.Multiply },
            { Microsoft.OData.UriParser.BinaryOperatorKind.NotEqual, ExpressionType.NotEqual },
            { Microsoft.OData.UriParser.BinaryOperatorKind.Or, ExpressionType.OrElse },
            { Microsoft.OData.UriParser.BinaryOperatorKind.Subtract, ExpressionType.Subtract },
        };

        private IEdmModel _model;

        private Stack<Dictionary<string, ParameterExpression>> _parametersStack;
        private Dictionary<string, ParameterExpression> _lambdaParameters;

        private ODataQuerySettings _querySettings;
        private IAssembliesResolver _assembliesResolver;

        private FilterBinder(IEdmModel model, IAssembliesResolver assembliesResolver, ODataQuerySettings querySettings)
            : this(model, querySettings)
        {
            _assembliesResolver = assembliesResolver;
        }

        private FilterBinder(IEdmModel model, ODataQuerySettings querySettings)
        {
            Contract.Assert(model != null);
            Contract.Assert(querySettings != null);
            Contract.Assert(querySettings.HandleNullPropagation != HandleNullPropagationOption.Default);

            IsOfTypesList = new List<string>();
            _querySettings = querySettings;
            _parametersStack = new Stack<Dictionary<string, ParameterExpression>>();
            _model = model;
        }

        /// <summary>
        /// Преобразование $orderby в linq-выражение.
        /// </summary>
        /// <param name="orderBy">Результат парсинга $orderby.</param>
        /// <param name="elementType">Тип элементов.</param>
        /// <param name="model">Edm-модель.</param>
        /// <param name="querySettings">Настойки запроса OData.</param>
        /// <returns>Linq-выражение полученное на основе $orderby.</returns>
        public static LambdaExpression Bind(
            Microsoft.OData.UriParser.OrderByClause orderBy, Type elementType, IEdmModel model, ODataQuerySettings querySettings)
        {
            Contract.Assert(orderBy != null);
            Contract.Assert(elementType != null);
            Contract.Assert(model != null);
            Contract.Assert(querySettings != null);

            FilterBinder binder = new FilterBinder(model, querySettings);
            LambdaExpression orderByLambda = binder.BindExpression(orderBy.Expression, orderBy.RangeVariable, elementType);
            return orderByLambda;
        }

        /// <summary>
        /// Преобразует выражение определяемое в $filter в linq-выражение.
        /// </summary>
        /// <param name="filterClause">Выражение определяемое в $filter.</param>
        /// <param name="filterType">Тип элементов в последовательности.</param>
        /// <param name="model">Edm-модель.</param>
        /// <param name="assembliesResolver">AssembliesResolver</param>
        /// <param name="querySettings">Настройки запроса</param>
        /// <returns>Экземпляр FilterBinder после выполнения преобразования из $filter в linq-выражение.</returns>
        public static FilterBinder Transform(
            Microsoft.OData.UriParser.FilterClause filterClause,
            Type filterType,
            IEdmModel model,
            IAssembliesResolver assembliesResolver,
            ODataQuerySettings querySettings)
        {
            if (filterClause == null)
            {
                throw Error.ArgumentNull("filterClause");
            }

            if (filterType == null)
            {
                throw Error.ArgumentNull("filterType");
            }

            if (model == null)
            {
                throw Error.ArgumentNull("model");
            }

            if (assembliesResolver == null)
            {
                throw Error.ArgumentNull("assembliesResolver");
            }

            FilterBinder binder = new FilterBinder(model, assembliesResolver, querySettings);
            binder.LinqExpression = binder.BindExpression(filterClause.Expression, filterClause.RangeVariable, filterType);
            binder.LinqExpression = Expression.Lambda(binder.ApplyNullPropagationForFilterBody(binder.LinqExpression.Body), binder.LinqExpression.Parameters);
            Type expectedFilterType = typeof(Func<,>).MakeGenericType(filterType, typeof(bool));
            if (binder.LinqExpression.Type != expectedFilterType)
            {
                throw Error.Argument("filterType", SRResources.CannotCastFilter, binder.LinqExpression.Type.FullName, expectedFilterType.FullName);
            }

            return binder;
        }

        private static IEnumerable<Expression> ExtractValueFromNullableArguments(IEnumerable<Expression> arguments)
        {
            return arguments.Select(arg => ExtractValueFromNullableExpression(arg));
        }

        private static Expression ExtractValueFromNullableExpression(Expression source)
        {
            return Nullable.GetUnderlyingType(source.Type) != null ? Expression.Property(source, "Value") : source;
        }

        private static Expression CheckIfArgumentsAreNull(Expression[] arguments)
        {
            if (arguments.Any(arg => arg == _nullConstant))
            {
                return _trueConstant;
            }

            arguments =
                arguments
                .Select(arg => CheckForNull(arg))
                .Where(arg => arg != null)
                .ToArray();

            if (arguments.Any())
            {
                return arguments
                    .Aggregate((left, right) => Expression.OrElse(left, right));
            }
            else
            {
                return _falseConstant;
            }
        }

        private static Expression CheckForNull(Expression expression)
        {
            if (IsNullable(expression.Type) && expression.NodeType != ExpressionType.Constant)
            {
                return Expression.Equal(expression, Expression.Constant(null));
            }
            else
            {
                return null;
            }
        }

        private static Expression Any(Expression source, Expression filter)
        {
            Contract.Assert(source != null);
            Type elementType;
            source.Type.IsCollection(out elementType);
            Contract.Assert(elementType != null);

            if (filter == null)
            {
                if (IsIQueryable(source.Type))
                {
                    return Expression.Call(null, ExpressionHelperMethods.QueryableEmptyAnyGeneric.MakeGenericMethod(elementType), source);
                }
                else
                {
                    return Expression.Call(null, ExpressionHelperMethods.EnumerableEmptyAnyGeneric.MakeGenericMethod(elementType), source);
                }
            }
            else
            {
                if (IsIQueryable(source.Type))
                {
                    return Expression.Call(null, ExpressionHelperMethods.QueryableNonEmptyAnyGeneric.MakeGenericMethod(elementType), source, filter);
                }
                else
                {
                    return Expression.Call(null, ExpressionHelperMethods.EnumerableNonEmptyAnyGeneric.MakeGenericMethod(elementType), source, filter);
                }
            }
        }

        private static Expression All(Expression source, Expression filter)
        {
            Contract.Assert(source != null);
            Contract.Assert(filter != null);

            Type elementType;
            source.Type.IsCollection(out elementType);
            Contract.Assert(elementType != null);

            if (IsIQueryable(source.Type))
            {
                return Expression.Call(null, ExpressionHelperMethods.QueryableAllGeneric.MakeGenericMethod(elementType), source, filter);
            }
            else
            {
                return Expression.Call(null, ExpressionHelperMethods.EnumerableAllGeneric.MakeGenericMethod(elementType), source, filter);
            }
        }

        private static bool IsNullable(Type t)
        {
            if (!t.IsValueType || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)))
            {
                return true;
            }

            if (t == typeof(ICSSoft.STORMNET.UserDataTypes.NullableDateTime))
            {
                return true;
            }

            return false;
        }

        private static Type ToNullable(Type t)
        {
            if (IsNullable(t))
            {
                return t;
            }
            else
            {
                return typeof(Nullable<>).MakeGenericType(t);
            }
        }

        private static Expression ToNullable(Expression expression)
        {
            if (!IsNullable(expression.Type))
            {
                return Expression.Convert(expression, ToNullable(expression.Type));
            }

            return expression;
        }

        private static bool IsIQueryable(Type type)
        {
            return typeof(IQueryable).IsAssignableFrom(type);
        }

        private static bool IsDoubleOrDecimal(Type type)
        {
            return IsType<double>(type) || IsType<decimal>(type);
        }

        private static bool IsDateRelated(Type type)
        {
            return IsType<Microsoft.OData.Edm.Date>(type) || IsType<DateTime>(type) || IsType<DateTimeOffset>(type);
        }

        private static bool IsTimeRelated(Type type)
        {
            return IsType<Microsoft.OData.Edm.TimeOfDay>(type) || IsType<DateTime>(type) || IsType<DateTimeOffset>(type);
        }

        private static bool IsDateOrOffset(Type type)
        {
            return IsType<DateTime>(type) || IsType<DateTimeOffset>(type);
        }

        private static bool IsDateTime(Type type)
        {
            return IsType<DateTime>(type);
        }

        private static bool IsTimeOfDay(Type type)
        {
            return IsType<Microsoft.OData.Edm.TimeOfDay>(type);
        }

        private static bool IsDate(Type type)
        {
            return IsType<Microsoft.OData.Edm.Date>(type);
        }

        private static bool IsInteger(Type type)
        {
            return IsType<short>(type) || IsType<int>(type) || IsType<long>(type);
        }

        private static bool IsType<T>(Type type)
            where T : struct
        {
            return type == typeof(T) || type == typeof(T?);
        }

        private static Expression ConvertNull(Expression expression, Type type)
        {
            ConstantExpression constantExpression = expression as ConstantExpression;
            if (constantExpression != null && constantExpression.Value == null)
            {
                return Expression.Constant(null, type);
            }
            else
            {
                return expression;
            }
        }

        // Extract the constant that would have been encapsulated into LinqParameterContainer if this
        // expression represents it else return null.
        private static object ExtractParameterizedConstant(Expression expression)
        {
            if (expression.NodeType == ExpressionType.MemberAccess)
            {
                MemberExpression memberAccess = expression as MemberExpression;
                Contract.Assert(memberAccess != null);
                if (memberAccess.Expression.NodeType == ExpressionType.Constant)
                {
                    ConstantExpression constant = memberAccess.Expression as ConstantExpression;
                    Contract.Assert(constant != null);
                    Contract.Assert(constant.Value != null);
                    LinqParameterContainer value = constant.Value as LinqParameterContainer;
                    Contract.Assert(value != null, "Constants are already embedded into LinqParameterContainer");

                    return value.Property;
                }
            }

            return null;
        }

        private static Expression ConvertToEnumUnderlyingType(Expression expression, Type enumType, Type enumUnderlyingType)
        {
            object parameterizedConstantValue = ExtractParameterizedConstant(expression);
            if (parameterizedConstantValue != null)
            {
                string enumStringValue = parameterizedConstantValue as string;
                if (enumStringValue != null)
                {
                    return Expression.Constant(
                        Convert.ChangeType(
                            Enum.Parse(enumType, enumStringValue), enumUnderlyingType, CultureInfo.InvariantCulture));
                }
                else
                {
                    // enum member value
                    return Expression.Constant(
                        Convert.ChangeType(
                            parameterizedConstantValue, enumUnderlyingType, CultureInfo.InvariantCulture));
                }
            }
            else if (expression.Type == enumType)
            {
                return Expression.Convert(expression, enumUnderlyingType);
            }
            else if (Nullable.GetUnderlyingType(expression.Type) == enumType)
            {
                return Expression.Convert(expression, typeof(Nullable<>).MakeGenericType(enumUnderlyingType));
            }
            else if (expression.NodeType == ExpressionType.Constant && ((ConstantExpression)expression).Value == null)
            {
                return expression;
            }
            else
            {
                throw Error.NotSupported(SRResources.ConvertToEnumFailed, enumType, expression.Type);
            }
        }

        private static void ValidateAllStringArguments(string functionName, Expression[] arguments)
        {
            if (arguments.Any(arg => arg.Type != typeof(string)))
            {
                throw new ODataException(Error.Format(SRResources.FunctionNotSupportedOnEnum, functionName));
            }
        }

        private static Expression BindCastToStringType(Expression source)
        {
            Expression sourceValue;

            if (source.Type.IsGenericType && source.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (TypeHelper.IsEnum(source.Type))
                {
                    // Entity Framework doesn't have ToString method for enum types.
                    // Convert enum types to their underlying numeric types.
                    sourceValue = Expression.Convert(
                        Expression.Property(source, "Value"),
                        Enum.GetUnderlyingType(TypeHelper.GetUnderlyingTypeOrSelf(source.Type)));
                }
                else
                {
                    // Entity Framework has ToString method for numeric types.
                    sourceValue = Expression.Property(source, "Value");
                }

                // Entity Framework doesn't have ToString method for nullable numeric types.
                // Call ToString method on non-nullable numeric types.
                return Expression.Condition(
                    Expression.Property(source, "HasValue"),
                    Expression.Call(sourceValue, "ToString", typeArguments: null, arguments: null),
                    Expression.Constant(null, typeof(string)));
            }
            else
            {
                sourceValue = TypeHelper.IsEnum(source.Type) ?
                    Expression.Convert(source, Enum.GetUnderlyingType(source.Type)) :
                    source;
                return Expression.Call(sourceValue, "ToString", typeArguments: null, arguments: null);
            }
        }

        private static Expression DateTimeOffsetToDateTime(Expression expression)
        {
            var unaryExpression = expression as UnaryExpression;
            if (unaryExpression != null)
            {
                if (Nullable.GetUnderlyingType(unaryExpression.Type) == unaryExpression.Operand.Type)
                {
                    // this is a cast from T to Nullable<T> which is redundant.
                    expression = unaryExpression.Operand;
                }
            }

            var parameterizedConstantValue = ExtractParameterizedConstant(expression);
            var dto = parameterizedConstantValue as DateTimeOffset?;
            if (dto != null)
            {
                expression = Expression.Constant(EdmPrimitiveHelpers.ConvertPrimitiveValue(dto.Value, typeof(DateTime)));
            }

            return expression;
        }

        private static Expression OfType(Expression source, Type elementType)
        {
            Contract.Assert(source != null);
            Contract.Assert(elementType != null);

            if (IsIQueryable(source.Type))
            {
                return Expression.Call(null, ExpressionHelperMethods.QueryableOfType.MakeGenericMethod(elementType), source);
            }
            else
            {
                return Expression.Call(null, ExpressionHelperMethods.EnumerableOfType.MakeGenericMethod(elementType), source);
            }
        }

        private Expression Bind(QueryNode node)
        {
            // Recursion guard to avoid stack overflows
            RuntimeHelpers.EnsureSufficientExecutionStack();

            CollectionNode collectionNode = node as CollectionNode;
            SingleValueNode singleValueNode = node as SingleValueNode;

            if (collectionNode != null)
            {
                switch (node.Kind)
                {
                    case QueryNodeKind.CollectionNavigationNode:
                        CollectionNavigationNode navigationNode = node as CollectionNavigationNode;
                        return BindNavigationPropertyNode(navigationNode.Source, navigationNode.NavigationProperty);

                    case QueryNodeKind.CollectionPropertyAccess:
                        return BindCollectionPropertyAccessNode(node as CollectionPropertyAccessNode);

                    //case Microsoft.OData.UriParser.QueryNodeKind.EntityCollectionCast:
                    //    return BindEntityCollectionCastNode(node as EntityCollectionCastNode);

                    case QueryNodeKind.CollectionFunctionCall:
                    //case Microsoft.OData.UriParser.QueryNodeKind.EntityCollectionFunctionCall:
                    //case QueryNodeKind.CollectionOpenPropertyAccess:
                    //case Microsoft.OData.UriParser.QueryNodeKind.CollectionPropertyCast:
                    // Unused or have unknown uses.
                    default:
                        throw Error.NotSupported(SRResources.QueryNodeBindingNotSupported, node.Kind, typeof(FilterBinder).Name);
                }
            }
            else if (singleValueNode != null)
            {
                switch (node.Kind)
                {
                    case QueryNodeKind.BinaryOperator:
                        return BindBinaryOperatorNode(node as BinaryOperatorNode);

                    case QueryNodeKind.Constant:
                        return BindConstantNode(node as ConstantNode);

                    case QueryNodeKind.Convert:
                        return BindConvertNode(node as ConvertNode);

                    //case QueryNodeKind.EntityRangeVariableReference:
                    //    return BindRangeVariable((node as  EntityRangeVariableReferenceNode).RangeVariable);

                    //case QueryNodeKind.NonentityRangeVariableReference:
                    //    return BindRangeVariable((node as NonentityRangeVariableReferenceNode).RangeVariable);

                    case QueryNodeKind.SingleValuePropertyAccess:
                        return BindPropertyAccessQueryNode(node as SingleValuePropertyAccessNode);

                    case QueryNodeKind.SingleValueOpenPropertyAccess:
                        return BindDynamicPropertyAccessQueryNode(node as SingleValueOpenPropertyAccessNode);

                    case QueryNodeKind.UnaryOperator:
                        return BindUnaryOperatorNode(node as UnaryOperatorNode);

                    case QueryNodeKind.SingleValueFunctionCall:
                        return BindSingleValueFunctionCallNode(node as SingleValueFunctionCallNode);

                    case QueryNodeKind.SingleNavigationNode:
                        SingleNavigationNode navigationNode = node as SingleNavigationNode;
                        return BindNavigationPropertyNode(navigationNode.Source, navigationNode.NavigationProperty);

                    case QueryNodeKind.Any:
                        return BindAnyNode(node as AnyNode);

                    case QueryNodeKind.All:
                        return BindAllNode(node as AllNode);

                    //case QueryNodeKind.SingleEntityCast:
                    //    return BindSingleEntityCastNode(node as  Microsoft.Data.OData.Query.SemanticAst.SingleEntityCastNode);

                    //case QueryNodeKind.SingleEntityFunctionCall:
                    //    return BindSingleEntityFunctionCallNode(node as SingleEntityFunctionCallNode);

                    case QueryNodeKind.NamedFunctionParameter:
                    //case QueryNodeKind.ParameterAlias:
                    //case QueryNodeKind.EntitySet:
                    //case QueryNodeKind.KeyLookup:
                    //case QueryNodeKind.SearchTerm:
                    //case QueryNodeKind.SingleValueCast:
                    // Unused or have unknown uses.
                    default:
                        throw Error.NotSupported(SRResources.QueryNodeBindingNotSupported, node.Kind, typeof(FilterBinder).Name);
                }
            }
            else
            {
                throw Error.NotSupported(SRResources.QueryNodeBindingNotSupported, node.Kind, typeof(FilterBinder).Name);
            }
        }

        private Expression BindDynamicPropertyAccessQueryNode(SingleValueOpenPropertyAccessNode openNode)
        {
            var prop = GetDynamicPropertyContainer(openNode);
            var propertyAccessExpression = BindPropertyAccessExpression(openNode, prop);
            var readDictionaryIndexerExpression = Expression.Property(
                propertyAccessExpression,
                _dictionaryStringObjectIndexerName,
                Expression.Constant(openNode.Name));
            var containsKeyExpression = Expression.Call(
                propertyAccessExpression,
                propertyAccessExpression.Type.GetMethod("ContainsKey"),
                Expression.Constant(openNode.Name));
            var nullExpression = Expression.Constant(null);

            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
            {
                var dynamicDictIsNotNull = Expression.NotEqual(propertyAccessExpression, Expression.Constant(null));
                var dynamicDictIsNotNullAndContainsKey = Expression.AndAlso(dynamicDictIsNotNull, containsKeyExpression);
                return Expression.Condition(
                    dynamicDictIsNotNullAndContainsKey,
                    readDictionaryIndexerExpression,
                    nullExpression);
            }
            else
            {
                return Expression.Condition(
                    containsKeyExpression,
                    readDictionaryIndexerExpression,
                    nullExpression);
            }
        }

        private Expression BindPropertyAccessExpression(Microsoft.OData.UriParser.SingleValueOpenPropertyAccessNode openNode, PropertyInfo prop)
        {
            var source = Bind(openNode.Source);
            Expression propertyAccessExpression;
            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True &&
                IsNullable(source.Type) && source != _lambdaParameters[ODataItParameterName])
            {
                propertyAccessExpression = Expression.Property(RemoveInnerNullPropagation(source), prop.Name);
            }
            else
            {
                propertyAccessExpression = Expression.Property(source, prop.Name);
            }

            return propertyAccessExpression;
        }

        private PropertyInfo GetDynamicPropertyContainer(SingleValueOpenPropertyAccessNode openNode)
        {
            IEdmStructuredType edmStructuredType;
            var edmTypeReference = openNode.Source.TypeReference;
            if (edmTypeReference.IsEntity())
            {
                edmStructuredType = edmTypeReference.AsEntity().EntityDefinition();
            }
            else if (edmTypeReference.IsComplex())
            {
                edmStructuredType = edmTypeReference.AsComplex().ComplexDefinition();
            }
            else
            {
                throw Error.NotSupported(SRResources.QueryNodeBindingNotSupported, openNode.Kind, typeof(FilterBinder).Name);
            }

            var prop = EdmLibHelpers.GetDynamicPropertyDictionary(edmStructuredType, _model);
            return prop;
        }

        //private Expression BindSingleEntityFunctionCallNode(SingleEntityFunctionCallNode node)
        //{
        //    switch (node.Name)
        //    {
        //        case ClrCanonicalFunctions.CastFunctionName:
        //            return BindSingleEntityCastFunctionCall(node);
        //        default:
        //            throw Error.NotSupported(SRResources.ODataFunctionNotSupported, node.Name);
        //    }
        //}

        //private Expression BindSingleEntityCastFunctionCall(SingleEntityFunctionCallNode node)
        //{
        //    Contract.Assert(node.Name == ClrCanonicalFunctions.CastFunctionName);

        //    Expression[] arguments = BindArguments(node.Arguments);

        //    Contract.Assert(arguments.Length == 2);

        //    string targetEdmTypeName = (string)((ConstantNode)node.Arguments.Last()).Value;
        //    IEdmType targetEdmType = _model.FindType(targetEdmTypeName);
        //    Type targetClrType = null;

        //    if (targetEdmType != null)
        //    {
        //        targetClrType = EdmLibHelpers.GetClrType(targetEdmType.ToEdmTypeReference(false), _model);
        //    }

        //    if (arguments[0].Type == targetClrType)
        //    {
        //        // We only support to cast Entity type to the same type now.
        //        return arguments[0];
        //    }
        //    else
        //    {
        //        // Cast fails and return null.
        //        return _nullConstant;
        //    }
        //}

        //private Expression BindSingleEntityCastNode(SingleEntityCastNode node)
        //{
        //    IEdmEntityTypeReference entity = node.EntityTypeReference;
        //    Contract.Assert(entity != null, "NS casts can contain only entity types");

        //    Type clrType = EdmLibHelpers.GetClrType(entity, _model);

        //    Expression source = BindCastSourceNode(node.Source);
        //    return Expression.TypeAs(source, clrType);
        //}

        //private Expression BindEntityCollectionCastNode(EntityCollectionCastNode node)
        //{
        //    IEdmEntityTypeReference entity = node.EntityItemType;
        //    Contract.Assert(entity != null, "NS casts can contain only entity types");

        //    Type clrType = EdmLibHelpers.GetClrType(entity, _model);

        //    Expression source = BindCastSourceNode(node.Source);
        //    return OfType(source, clrType);
        //}

        private Expression BindCastSourceNode(QueryNode sourceNode)
        {
            Expression source;
            if (sourceNode == null)
            {
                // if the cast is on the root i.e $it (~/Products?$filter=NS.PopularProducts/.....),
                // source would be null. So bind null to '$it'.
                source = _lambdaParameters[ODataItParameterName];
            }
            else
            {
                source = Bind(sourceNode);
            }

            return source;
        }

        private Expression BindNavigationPropertyNode(QueryNode sourceNode, IEdmNavigationProperty navigationProperty)
        {
            Expression source;

            // TODO: bug in uri parser is causing this property to be null for the root property.
            if (sourceNode == null)
            {
                source = _lambdaParameters[ODataItParameterName];
            }
            else
            {
                source = Bind(sourceNode);
            }

            return CreatePropertyAccessExpression(source, navigationProperty);
        }

        private Expression BindBinaryOperatorNode(BinaryOperatorNode binaryOperatorNode)
        {
            Expression left = Bind(binaryOperatorNode.Left);
            Expression right = Bind(binaryOperatorNode.Right);

            // handle null propagation only if either of the operands can be null
            bool isNullPropagationRequired = _querySettings.HandleNullPropagation == HandleNullPropagationOption.True && (IsNullable(left.Type) || IsNullable(right.Type));
            if (isNullPropagationRequired)
            {
                // |----------------------------------------------------------------|
                // |SQL 3VL truth table.                                            |
                // |----------------------------------------------------------------|
                // |p       |    q      |    p OR q     |    p AND q    |    p = q  |
                // |----------------------------------------------------------------|
                // |True    |   True    |   True        |   True        |   True    |
                // |True    |   False   |   True        |   False       |   False   |
                // |True    |   NULL    |   True        |   NULL        |   NULL    |
                // |False   |   True    |   True        |   False       |   False   |
                // |False   |   False   |   False       |   False       |   True    |
                // |False   |   NULL    |   NULL        |   False       |   NULL    |
                // |NULL    |   True    |   True        |   NULL        |   NULL    |
                // |NULL    |   False   |   NULL        |   False       |   NULL    |
                // |NULL    |   NULL    |   Null        |   NULL        |   NULL    |
                // |--------|-----------|---------------|---------------|-----------|

                // before we start with null propagation, convert the operators to nullable if already not.
                left = ToNullable(left);
                right = ToNullable(right);

                bool liftToNull = true;
                if (left == _nullConstant || right == _nullConstant)
                {
                    liftToNull = false;
                }

                // Expression trees do a very good job of handling the 3VL truth table if we pass liftToNull true.
                return CreateBinaryExpression(binaryOperatorNode.OperatorKind, left, right, liftToNull: liftToNull);
            }
            else
            {
                return CreateBinaryExpression(binaryOperatorNode.OperatorKind, left, right, liftToNull: false);
            }
        }

        private Expression BindConstantNode(ConstantNode constantNode)
        {
            Contract.Assert(constantNode != null);

            // no need to parameterize null's as there cannot be multiple values for null.
            if (constantNode.Value == null)
            {
                return _nullConstant;
            }

            Type constantType = EdmLibHelpers.GetClrType(constantNode.TypeReference, _model, _assembliesResolver);
            object value = constantNode.Value;

            if (constantNode.TypeReference != null && constantNode.TypeReference.IsEnum())
            {
                ODataEnumValue odataEnumValue = (ODataEnumValue)value;
                string strValue = odataEnumValue.Value;
                Contract.Assert(strValue != null);

                constantType = Nullable.GetUnderlyingType(constantType) ?? constantType;
                value = Enum.Parse(constantType, strValue);
            }

            if (_querySettings.EnableConstantParameterization)
            {
                return LinqParameterContainer.Parameterize(constantType, value);
            }
            else
            {
                return Expression.Constant(value, constantType);
            }
        }

        private Expression BindConvertNode(ConvertNode convertNode)
        {
            Contract.Assert(convertNode != null);
            Contract.Assert(convertNode.TypeReference != null);

            Expression source = Bind(convertNode.Source);

            Type conversionType = EdmLibHelpers.GetClrType(convertNode.TypeReference, _model, _assembliesResolver);

            if (conversionType == typeof(bool?) && source.Type == typeof(bool))
            {
                // we handle null propagation ourselves. So, if converting from bool to Nullable<bool> ignore.
                return source;
            }
            else if (conversionType == typeof(DateTimeOffset?) &&
                (source.Type == typeof(DateTime) || source.Type == typeof(DateTime?)))
            {
                return source;
            }
            else if (conversionType == typeof(Microsoft.OData.Edm.Date?) &&
                (source.Type == typeof(DateTimeOffset?) || source.Type == typeof(DateTime?)))
            {
                return source;
            }
            else if (conversionType == typeof(Microsoft.OData.Edm.TimeOfDay?) &&
                (source.Type == typeof(DateTimeOffset?) || source.Type == typeof(DateTime?)))
            {
                return source;
            }
            else if (source == _nullConstant)
            {
                return source;
            }
            else
            {
                if (TypeHelper.IsEnum(source.Type))
                {
                    // we handle enum conversions ourselves
                    return source;
                }
                else
                {
                    // if a cast is from Nullable<T> to Non-Nullable<T> we need to check if source is null
                    if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True
                        && IsNullable(source.Type) && !IsNullable(conversionType))
                    {
                        // source == null ? null : source.Value
                        return
                            Expression.Condition(
                            test: CheckForNull(source),
                            ifTrue: Expression.Constant(null, ToNullable(conversionType)),
                            ifFalse: Expression.Convert(ExtractValueFromNullableExpression(source), ToNullable(conversionType)));
                    }
                    else
                    {
                        return Expression.Convert(source, conversionType);
                    }
                }
            }
        }

        private LambdaExpression BindExpression(SingleValueNode expression, Microsoft.OData.UriParser.RangeVariable rangeVariable, Type elementType)
        {
            ParameterExpression filterParameter = Expression.Parameter(elementType, rangeVariable.Name);
            _lambdaParameters = new Dictionary<string, ParameterExpression>();
            _lambdaParameters.Add(rangeVariable.Name, filterParameter);

            Expression body = Bind(expression);
            return Expression.Lambda(body, filterParameter);
        }

        private Expression ApplyNullPropagationForFilterBody(Expression body)
        {
            if (IsNullable(body.Type))
            {
                if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
                {
                    // handle null as false
                    // body => body == true. passing liftToNull:false would convert null to false.
                    body = Expression.Equal(body, Expression.Constant(true, typeof(bool?)), liftToNull: false, method: null);
                }
                else
                {
                    body = Expression.Convert(body, typeof(bool));
                }
            }

            return body;
        }

        private Expression BindRangeVariable(RangeVariable rangeVariable)
        {
            ParameterExpression parameter = _lambdaParameters[rangeVariable.Name];
            return ConvertNonStandardPrimitives(parameter);
        }

        private Expression BindCollectionPropertyAccessNode(CollectionPropertyAccessNode propertyAccessNode)
        {
            Expression source = Bind(propertyAccessNode.Source);
            return CreatePropertyAccessExpression(source, propertyAccessNode.Property);
        }

        private Expression BindPropertyAccessQueryNode(SingleValuePropertyAccessNode propertyAccessNode)
        {
            Expression source = Bind(propertyAccessNode.Source);
            Expression ret = CreatePropertyAccessExpression(source, propertyAccessNode.Property);
            if (propertyAccessNode.Property.Name == "__PrimaryKey")
            {
                if (propertyAccessNode.Property.Type.Definition.FullTypeName() == "Edm.Guid")
                {
                    Type targetClrType = typeof(Nullable<>).MakeGenericType(typeof(Guid));
                    ret = Expression.Convert(ret, targetClrType);
                }
                if (propertyAccessNode.Property.Type.Definition.FullTypeName() == "Edm.String")
                {
                    Type targetClrType = typeof(string);
                    ret = Expression.Convert(ret, targetClrType);
                }
            }

            return ret;
        }

        private Expression CreatePropertyAccessExpression(Expression source, IEdmProperty property)
        {
            string propertyName = EdmLibHelpers.GetClrPropertyName(property, _model);
            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True && IsNullable(source.Type) && source != _lambdaParameters[ODataItParameterName])
            {
                Expression propertyAccessExpression;
                if (System.Type.GetType("Mono.Runtime") != null)
                {
                    PropertyInfo pi = source.Type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                    propertyAccessExpression = Expression.Property(RemoveInnerNullPropagation(source), pi);
                }
                else
                {
                    propertyAccessExpression = Expression.Property(RemoveInnerNullPropagation(source), propertyName);
                }

                // source.property => source == null ? null : [CastToNullable]RemoveInnerNullPropagation(source).property
                // Notice that we are checking if source is null already. so we can safely remove any null checks when doing source.Property
                Expression ifFalse = ToNullable(ConvertNonStandardPrimitives(propertyAccessExpression));
                return
                    Expression.Condition(
                        test: Expression.Equal(source, _nullConstant),
                        ifTrue: Expression.Constant(null, ifFalse.Type),
                        ifFalse: ifFalse);
            }
            else
            {
                if (System.Type.GetType("Mono.Runtime") != null)
                {
                    PropertyInfo pi = source.Type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                    var exp = Expression.Property(source, pi);
                    return ConvertNonStandardPrimitives(exp);
                }
                else
                {
                    return ConvertNonStandardPrimitives(Expression.Property(source, propertyName));
                }
            }
        }

        private Expression BindUnaryOperatorNode(UnaryOperatorNode unaryOperatorNode)
        {
            // No need to handle null-propagation here as CLR already handles it.
            // !(null) = null
            // -(null) = null
            Expression inner = Bind(unaryOperatorNode.Operand);
            switch (unaryOperatorNode.OperatorKind)
            {
                case Microsoft.OData.UriParser.UnaryOperatorKind.Negate:
                    return Expression.Negate(inner);

                case Microsoft.OData.UriParser.UnaryOperatorKind.Not:
                    return Expression.Not(inner);

                default:
                    throw Error.NotSupported(SRResources.QueryNodeBindingNotSupported, unaryOperatorNode.Kind, typeof(FilterBinder).Name);
            }
        }

        private Expression BindSingleValueFunctionCallNode(SingleValueFunctionCallNode node)
        {
            switch (node.Name)
            {
                case ClrCanonicalFunctions.StartswithFunctionName:
                    return BindStartsWith(node);

                case ClrCanonicalFunctions.EndswithFunctionName:
                    return BindEndsWith(node);

                case ClrCanonicalFunctions.ContainsFunctionName:
                    return BindContains(node);

                case ClrCanonicalFunctions.SubstringFunctionName:
                    return BindSubstring(node);

                case ClrCanonicalFunctions.LengthFunctionName:
                    return BindLength(node);

                case ClrCanonicalFunctions.IndexofFunctionName:
                    return BindIndexOf(node);

                case ClrCanonicalFunctions.TolowerFunctionName:
                    return BindToLower(node);

                case ClrCanonicalFunctions.ToupperFunctionName:
                    return BindToUpper(node);

                case ClrCanonicalFunctions.TrimFunctionName:
                    return BindTrim(node);

                case ClrCanonicalFunctions.ConcatFunctionName:
                    return BindConcat(node);

                case ClrCanonicalFunctions.YearFunctionName:
                case ClrCanonicalFunctions.MonthFunctionName:
                case ClrCanonicalFunctions.DayFunctionName:
                    return BindDateRelatedProperty(node); // Date & DateTime & DateTimeOffset

                case ClrCanonicalFunctions.HourFunctionName:
                case ClrCanonicalFunctions.MinuteFunctionName:
                case ClrCanonicalFunctions.SecondFunctionName:
                    return BindTimeRelatedProperty(node); // TimeOfDay & DateTime & DateTimeOffset

                case ClrCanonicalFunctions.FractionalSecondsFunctionName:
                    return BindFractionalSeconds(node);

                case ClrCanonicalFunctions.RoundFunctionName:
                    return BindRound(node);

                case ClrCanonicalFunctions.FloorFunctionName:
                    return BindFloor(node);

                case ClrCanonicalFunctions.CeilingFunctionName:
                    return BindCeiling(node);

                case ClrCanonicalFunctions.CastFunctionName:
                    return BindCastSingleValue(node);

                case ClrCanonicalFunctions.DateFunctionName:
                    return BindDate(node);

                case ClrCanonicalFunctions.TimeFunctionName:
                    return BindTime(node);

                case "geo.intersects":
                    return BindGeoIntersects(node);
                case "geom.intersects":
                    return BindGeomIntersects(node);
                case "now":
                    return BindNow(node);
                case "isof":
                    ConstantNode lastParam = node.Parameters.LastOrDefault() as ConstantNode;
                    if (lastParam != null)
                    {
                        string type = lastParam.Value as string;
                        if (type != null && !IsOfTypesList.Any(it => it == type))
                            IsOfTypesList.Add(type);
                    }

                    return Expression.Constant(true, typeof(bool));

                default:
                    throw new NotImplementedException(Error.Format(SRResources.ODataFunctionNotSupported, node.Name));
            }
        }

        private Expression CreateFunctionCallWithNullPropagation(Expression functionCall, Expression[] arguments)
        {
            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
            {
                Expression test = CheckIfArgumentsAreNull(arguments);

                if (test == _falseConstant)
                {
                    // none of the arguments are/can be null.
                    // so no need to do any null propagation
                    return functionCall;
                }
                else
                {
                    // if one of the arguments is null, result is null (not defined)
                    return
                        Expression.Condition(
                        test: test,
                        ifTrue: Expression.Constant(null, ToNullable(functionCall.Type)),
                        ifFalse: ToNullable(functionCall));
                }
            }
            else
            {
                return functionCall;
            }
        }

        // we don't have to do null checks inside the function for arguments as we do the null checks before calling
        // the function when null propagation is enabled.
        // this method converts back "arg == null ? null : convert(arg)" to "arg"
        // Also, note that we can do this generically only because none of the odata functions that we support can take null
        // as an argument.
        private Expression RemoveInnerNullPropagation(Expression expression)
        {
            Contract.Assert(expression != null);

            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
            {
                // only null propagation generates conditional expressions
                if (expression.NodeType == ExpressionType.Conditional)
                {
                    // make sure to skip the DateTime IFF clause
                    ConditionalExpression conditionalExpr = (ConditionalExpression)expression;
                    if (conditionalExpr.Test.NodeType != ExpressionType.OrElse)
                    {
                        expression = conditionalExpr.IfFalse;
                        Contract.Assert(expression != null);

                        if (expression.NodeType == ExpressionType.Convert)
                        {
                            UnaryExpression unaryExpression = expression as UnaryExpression;
                            Contract.Assert(unaryExpression != null);

                            if (Nullable.GetUnderlyingType(unaryExpression.Type) == unaryExpression.Operand.Type)
                            {
                                // this is a cast from T to Nullable<T> which is redundant.
                                expression = unaryExpression.Operand;
                            }
                        }
                    }
                }
            }

            return expression;
        }

        // creates an expression for the corresponding OData function.
        private Expression MakeFunctionCall(MemberInfo member, params Expression[] arguments)
        {
            Contract.Assert(member.MemberType == MemberTypes.Property || member.MemberType == MemberTypes.Method);

            IEnumerable<Expression> functionCallArguments = arguments;
            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
            {
                // we don't have to check if the argument is null inside the function call as we do it already
                // before calling the function. So remove the redundant null checks.
                functionCallArguments = arguments.Select(a => RemoveInnerNullPropagation(a));
            }

            // if the argument is of type Nullable<T>, then translate the argument to Nullable<T>.Value as none
            // of the canonical functions have overloads for Nullable<> arguments.
            functionCallArguments = ExtractValueFromNullableArguments(functionCallArguments);

            Expression functionCall;
            if (member.MemberType == MemberTypes.Method)
            {
                MethodInfo method = member as MethodInfo;
                if (method.IsStatic)
                {
                    functionCall = Expression.Call(null, method, functionCallArguments);
                }
                else
                {
                    functionCall = Expression.Call(functionCallArguments.First(), method, functionCallArguments.Skip(1));
                }
            }
            else
            {
                // property
                functionCall = Expression.Property(functionCallArguments.First(), member as PropertyInfo);
            }

            return CreateFunctionCallWithNullPropagation(functionCall, arguments);
        }

        private Expression MakePropertyAccess(PropertyInfo propertyInfo, Expression argument)
        {
            Expression propertyArgument = argument;
            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
            {
                // we don't have to check if the argument is null inside the function call as we do it already
                // before calling the function. So remove the redundant null checks.
                propertyArgument = RemoveInnerNullPropagation(argument);
            }

            // if the argument is of type Nullable<T>, then translate the argument to Nullable<T>.Value as none
            // of the canonical functions have overloads for Nullable<> arguments.
            propertyArgument = ExtractValueFromNullableExpression(propertyArgument);

            return Expression.Property(propertyArgument, propertyInfo);
        }

        private Expression BindCastSingleValue(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == ClrCanonicalFunctions.CastFunctionName);

            Expression[] arguments = BindArguments(node.Parameters);
            Contract.Assert(arguments.Length == 1 || arguments.Length == 2);

            Expression source = arguments.Length == 1 ? _lambdaParameters[ODataItParameterName] : arguments[0];
            string targetTypeName = (string)((Microsoft.OData.UriParser.ConstantNode)node.Parameters.Last()).Value;
            IEdmType targetEdmType = _model.FindType(targetTypeName);
            Type targetClrType = null;

            if (targetEdmType != null)
            {
                IEdmTypeReference targetEdmTypeReference = targetEdmType.ToEdmTypeReference(false);
                targetClrType = EdmLibHelpers.GetClrType(targetEdmTypeReference, _model);

                if (source != _nullConstant)
                {
                    if (source.Type == targetClrType)
                    {
                        return source;
                    }

                    if ((!targetEdmTypeReference.IsPrimitive() && !targetEdmTypeReference.IsEnum()) ||
                        (source.Type != typeof(object) && EdmLibHelpers.GetEdmPrimitiveTypeOrNull(source.Type) == null && !TypeHelper.IsEnum(source.Type)))
                    {
                        // Cast fails and return null.
                        return _nullConstant;
                    }
                }
            }

            if (targetClrType == null || source == _nullConstant)
            {
                return _nullConstant;
            }

            if (targetClrType == typeof(string))
            {
                return BindCastToStringType(source);
            }
            else if (TypeHelper.IsEnum(targetClrType))
            {
                return BindCastToEnumType(source.Type, targetClrType, node.Parameters.First(), arguments.Length);
            }
            else
            {
                if (source.Type.IsNullable() && !targetClrType.IsNullable())
                {
                    // Make the target Clr type nullable to avoid failure while casting
                    // nullable source, whose value may be null, to a non-nullable type.
                    // For example: cast(NullableInt32Property,Edm.Int64)
                    // The target Clr type should be Nullable<Int64> rather than Int64.
                    targetClrType = typeof(Nullable<>).MakeGenericType(targetClrType);
                }

                try
                {
                    return Expression.Convert(source, targetClrType);
                }
                catch (InvalidOperationException)
                {
                    // Cast fails and return null.
                    return _nullConstant;
                }
            }
        }

        private Expression BindCastToEnumType(Type sourceType, Type targetClrType, QueryNode firstParameter, int parameterLength)
        {
            Type enumType = TypeHelper.GetUnderlyingTypeOrSelf(targetClrType);
            ConstantNode sourceNode = firstParameter as ConstantNode;

            if (parameterLength == 1 || sourceNode == null || sourceType != typeof(string))
            {
                // We only support to cast Enumeration type from constant string now,
                // because LINQ to Entities does not recognize the method Enum.TryParse.
                return _nullConstant;
            }
            else
            {
                object[] parameters = new[] { sourceNode.Value, Enum.ToObject(enumType, 0) };
                bool isSuccessful = (bool)_enumTryParseMethod.MakeGenericMethod(enumType).Invoke(null, parameters);

                if (isSuccessful)
                {
                    if (_querySettings.EnableConstantParameterization)
                    {
                        return LinqParameterContainer.Parameterize(targetClrType, parameters[1]);
                    }
                    else
                    {
                        return Expression.Constant(parameters[1], targetClrType);
                    }
                }
                else
                {
                    return _nullConstant;
                }
            }
        }

        private Expression BindCeiling(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "ceiling");

            Expression[] arguments = BindArguments(node.Parameters);

            Contract.Assert(arguments.Length == 1 && IsDoubleOrDecimal(arguments[0].Type));

            MethodInfo ceiling = IsType<double>(arguments[0].Type)
                ? ClrCanonicalFunctions.CeilingOfDouble
                : ClrCanonicalFunctions.CeilingOfDecimal;
            return MakeFunctionCall(ceiling, arguments);
        }

        private Expression BindFloor(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "floor");

            Expression[] arguments = BindArguments(node.Parameters);

            Contract.Assert(arguments.Length == 1 && IsDoubleOrDecimal(arguments[0].Type));

            MethodInfo floor = IsType<double>(arguments[0].Type)
                ? ClrCanonicalFunctions.FloorOfDouble
                : ClrCanonicalFunctions.FloorOfDecimal;
            return MakeFunctionCall(floor, arguments);
        }

        private Expression BindRound(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "round");

            Expression[] arguments = BindArguments(node.Parameters);

            Contract.Assert(arguments.Length == 1 && IsDoubleOrDecimal(arguments[0].Type));

            MethodInfo round = IsType<double>(arguments[0].Type)
                ? ClrCanonicalFunctions.RoundOfDouble
                : ClrCanonicalFunctions.RoundOfDecimal;
            return MakeFunctionCall(round, arguments);
        }

        private Expression BindNow(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "now");

            Expression[] arguments = BindArguments(node.Parameters);

            Contract.Assert(arguments.Length == 0);

            return Expression.Property(null, ClrCanonicalFunctions.Now);
        }

        private Expression BindDate(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "date");

            Expression[] arguments = BindArguments(node.Parameters);

            // We should support DateTime & DateTimeOffset even though DateTime is not part of OData v4 Spec.
            Contract.Assert(arguments.Length == 1 && IsDateOrOffset(arguments[0].Type));

            // EF doesn't support new Date(int, int, int), also doesn't support other property access, for example DateTime.Date.
            // Therefore, we just return the source (DateTime or DateTimeOffset).
            return arguments[0];
        }

        private Expression BindTime(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "time");

            Expression[] arguments = BindArguments(node.Parameters);

            // We should support DateTime & DateTimeOffset even though DateTime is not part of OData v4 Spec.
            Contract.Assert(arguments.Length == 1 && IsDateOrOffset(arguments[0].Type));

            // EF doesn't support new TimeOfDay(int, int, int, int), also doesn't support other property access, for example DateTimeOffset.DateTime.
            // Therefore, we just return the source (DateTime or DateTimeOffset).
            return arguments[0];
        }

        private Expression BindFractionalSeconds(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "fractionalseconds");

            Expression[] arguments = BindArguments(node.Parameters);
            Contract.Assert(arguments.Length == 1 && IsTimeRelated(arguments[0].Type));

            // We should support DateTime & DateTimeOffset even though DateTime is not part of OData v4 Spec.
            Expression parameter = arguments[0];

            PropertyInfo property;
            if (IsTimeOfDay(parameter.Type))
            {
                property = ClrCanonicalFunctions.TimeOfDayProperties[ClrCanonicalFunctions.MillisecondFunctionName];
            }
            else
            if (IsDateTime(parameter.Type))
            {
                property = ClrCanonicalFunctions.DateTimeProperties[ClrCanonicalFunctions.MillisecondFunctionName];
            }
            else
            {
                property = ClrCanonicalFunctions.DateTimeOffsetProperties[ClrCanonicalFunctions.MillisecondFunctionName];
            }

            // Millisecond
            Expression milliSecond = MakePropertyAccess(property, parameter);
            Expression decimalMilliSecond = Expression.Convert(milliSecond, typeof(decimal));
            Expression fractionalSeconds = Expression.Divide(decimalMilliSecond, Expression.Constant(1000m, typeof(decimal)));

            return CreateFunctionCallWithNullPropagation(fractionalSeconds, arguments);
        }

        private Expression BindDateRelatedProperty(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Expression[] arguments = BindArguments(node.Parameters);
            Contract.Assert(arguments.Length == 1 && IsDateRelated(arguments[0].Type));

            // We should support DateTime & DateTimeOffset even though DateTime is not part of OData v4 Spec.
            Expression parameter = arguments[0];

            PropertyInfo property;
            if (IsDate(parameter.Type))
            {
                Contract.Assert(ClrCanonicalFunctions.DateProperties.ContainsKey(node.Name));
                property = ClrCanonicalFunctions.DateProperties[node.Name];
            }
            else if (IsDateTime(parameter.Type))
            {
                Contract.Assert(ClrCanonicalFunctions.DateTimeProperties.ContainsKey(node.Name));
                property = ClrCanonicalFunctions.DateTimeProperties[node.Name];
            }
            else
            {
                Contract.Assert(ClrCanonicalFunctions.DateTimeOffsetProperties.ContainsKey(node.Name));
                property = ClrCanonicalFunctions.DateTimeOffsetProperties[node.Name];
            }

            return MakeFunctionCall(property, parameter);
        }

        private Expression BindTimeRelatedProperty(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Expression[] arguments = BindArguments(node.Parameters);
            Contract.Assert(arguments.Length == 1 && IsTimeRelated(arguments[0].Type));

            // We should support DateTime & DateTimeOffset even though DateTime is not part of OData v4 Spec.
            Expression parameter = arguments[0];

            PropertyInfo property;
            if (IsTimeOfDay(parameter.Type))
            {
                Contract.Assert(ClrCanonicalFunctions.TimeOfDayProperties.ContainsKey(node.Name));
                property = ClrCanonicalFunctions.TimeOfDayProperties[node.Name];
            }
            else if (IsDateTime(parameter.Type))
            {
                Contract.Assert(ClrCanonicalFunctions.DateTimeProperties.ContainsKey(node.Name));
                property = ClrCanonicalFunctions.DateTimeProperties[node.Name];
            }
            else
            {
                Contract.Assert(ClrCanonicalFunctions.DateTimeOffsetProperties.ContainsKey(node.Name));
                property = ClrCanonicalFunctions.DateTimeOffsetProperties[node.Name];
            }

            return MakeFunctionCall(property, parameter);
        }

        private Expression BindConcat(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "concat");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 2 && arguments[0].Type == typeof(string) && arguments[1].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.Concat, arguments);
        }

        private Expression BindTrim(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "trim");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 1 && arguments[0].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.Trim, arguments);
        }

        private Expression BindToUpper(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "toupper");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 1 && arguments[0].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.ToUpper, arguments);
        }

        private Expression BindToLower(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "tolower");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 1 && arguments[0].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.ToLower, arguments);
        }

        private Expression BindIndexOf(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "indexof");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 2 && arguments[0].Type == typeof(string) && arguments[1].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.IndexOf, arguments);
        }

        private Expression BindSubstring(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "substring");

            Expression[] arguments = BindArguments(node.Parameters);
            if (arguments[0].Type != typeof(string))
            {
                throw new ODataException(Error.Format(SRResources.FunctionNotSupportedOnEnum, node.Name));
            }

            Expression functionCall;
            if (arguments.Length == 2)
            {
                Contract.Assert(IsInteger(arguments[1].Type));

                // When null propagation is allowed, we use a safe version of String.Substring(int).
                // But for providers that would not recognize custom expressions like this, we map
                // directly to String.Substring(int)
                if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
                {
                    // Safe function is static and takes string "this" as first argument
                    functionCall = MakeFunctionCall(ClrCanonicalFunctions.SubstringStartNoThrow, arguments);
                }
                else
                {
                    functionCall = MakeFunctionCall(ClrCanonicalFunctions.SubstringStart, arguments);
                }
            }
            else
            {
                // arguments.Length == 3 implies String.Substring(int, int)
                Contract.Assert(arguments.Length == 3 && IsInteger(arguments[1].Type) && IsInteger(arguments[2].Type));

                // When null propagation is allowed, we use a safe version of String.Substring(int, int).
                // But for providers that would not recognize custom expressions like this, we map
                // directly to String.Substring(int, int)
                if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True)
                {
                    // Safe function is static and takes string "this" as first argument
                    functionCall = MakeFunctionCall(ClrCanonicalFunctions.SubstringStartAndLengthNoThrow, arguments);
                }
                else
                {
                    functionCall = MakeFunctionCall(ClrCanonicalFunctions.SubstringStartAndLength, arguments);
                }
            }

            return functionCall;
        }

        private Expression BindLength(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "length");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 1 && arguments[0].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.Length, arguments);
        }

        private Expression BindGeoIntersects(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "geo.intersects");

            Expression[] arguments = node.Parameters.OfType<Microsoft.OData.UriParser.NamedFunctionParameterNode>().Select(n => Bind(n.Value)).ToArray();

            Contract.Assert(arguments.Length == 2 &&
                (arguments[0].Type == typeof(Geography) || arguments[0].Type.IsSubclassOf(typeof(Geography))) &&
                arguments[1].Type == typeof(Geography) || arguments[1].Type.IsSubclassOf(typeof(Geography)));

            return MakeFunctionCall(ClrCanonicalFunctions.GeoIntersects, arguments[0], arguments[1]);
        }

        private Expression BindGeomIntersects(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "geom.intersects");

            Expression[] arguments = node.Parameters.OfType<NamedFunctionParameterNode>().Select(n => Bind(n.Value)).ToArray();

            Contract.Assert(arguments.Length == 2 &&
                (arguments[0].Type == typeof(Geometry) || arguments[0].Type.IsSubclassOf(typeof(Geometry))) &&
                arguments[1].Type == typeof(Geometry) || arguments[1].Type.IsSubclassOf(typeof(Geometry)));

            return MakeFunctionCall(ClrCanonicalFunctions.GeomIntersects, arguments[0], arguments[1]);
        }

        private Expression BindContains(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "contains");

            Expression[] arguments = BindArguments(node.Parameters);
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].Type == typeof(object))
                    arguments[i] = Expression.Convert(arguments[i], typeof(string));
            }

            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 2 && arguments[0].Type == typeof(string) && arguments[1].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.Contains, arguments[0], arguments[1]);
        }

        private Expression BindStartsWith(SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "startswith");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 2 && arguments[0].Type == typeof(string) && arguments[1].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.StartsWith, arguments);
        }

        private Expression BindEndsWith(Microsoft.OData.UriParser.SingleValueFunctionCallNode node)
        {
            Contract.Assert(node.Name == "endswith");

            Expression[] arguments = BindArguments(node.Parameters);
            ValidateAllStringArguments(node.Name, arguments);

            Contract.Assert(arguments.Length == 2 && arguments[0].Type == typeof(string) && arguments[1].Type == typeof(string));

            return MakeFunctionCall(ClrCanonicalFunctions.EndsWith, arguments);
        }

        private Expression BindHas(Expression left, Expression flag)
        {
            Contract.Assert(TypeHelper.IsEnum(left.Type));
            Contract.Assert(flag.Type == typeof(Enum));

            Expression[] arguments = new[] { left, flag };
            return MakeFunctionCall(ClrCanonicalFunctions.HasFlag, arguments);
        }

        private Expression[] BindArguments(IEnumerable<QueryNode> nodes)
        {
            return nodes.OfType<SingleValueNode>().Select(n => Bind(n)).ToArray();
        }

        private Expression BindAllNode(AllNode allNode)
        {
            ParameterExpression allIt = HandleLambdaParameters(allNode.RangeVariables);
            FillFilterDetailProperties(allNode);

            Expression source;
            Contract.Assert(allNode.Source != null);
            source = Bind(allNode.Source);

            Expression body = source;
            Contract.Assert(allNode.Body != null);

            body = Bind(allNode.Body);
            body = ApplyNullPropagationForFilterBody(body);
            body = Expression.Lambda(body, allIt);

            Expression all = All(source, body);

            ExitLamdbaScope();

            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True && IsNullable(source.Type))
            {
                // IFF(source == null) null; else Any(body);
                all = ToNullable(all);
                return Expression.Condition(
                    test: Expression.Equal(source, _nullConstant),
                    ifTrue: Expression.Constant(null, all.Type),
                    ifFalse: all);
            }
            else
            {
                return all;
            }
        }

        private void FillFilterDetailProperties(LambdaNode anyNode)
        {
            var firstNode = anyNode.Source as CollectionNavigationNode;
            if (firstNode != null)
            {
                throw new NotImplementedException();
                //var it = firstNode.Source as EntityRangeVariableReferenceNode;
                //if (it != null && it.Name == "$it")
                //{
                //    var binaryNode = anyNode.Body as BinaryOperatorNode;
                //    var propertyNodes = new SingleValuePropertyAccessNode[0];
                //    if (binaryNode != null)
                //    {
                //        propertyNodes = new SingleValuePropertyAccessNode[] { binaryNode.Left as SingleValuePropertyAccessNode, binaryNode.Right as SingleValuePropertyAccessNode };
                //    }

                //    var functionCallNode = anyNode.Body as SingleValueFunctionCallNode;
                //    if (functionCallNode != null && functionCallNode.Name == "contains")
                //    {
                //        var parameters = functionCallNode.Arguments.ToList();
                //        propertyNodes = new SingleValuePropertyAccessNode[] { parameters[0] as SingleValuePropertyAccessNode, parameters[1] as SingleValuePropertyAccessNode };
                //    }

                //    foreach (var propertyNode in propertyNodes)
                //    {
                //        if (propertyNode != null)
                //        {
                //            var masters = new List<string>();
                //            var navigationNode = propertyNode.Source as SingleNavigationNode;
                //            while (navigationNode != null)
                //            {
                //                masters.Insert(0, navigationNode.NavigationProperty.Name);
                //                navigationNode = navigationNode.Source as SingleNavigationNode;
                //            }

                //            masters.Insert(0, firstNode.NavigationProperty.Name);
                //            masters.Add(propertyNode.Property.Name);
                //            var nameProperty = string.Join(".", masters.ToArray());
                //            FilterDetailProperties.Add(nameProperty);
                //        }
                //    }
                //}
            }
        }

        private Expression BindAnyNode(AnyNode anyNode)
        {
            ParameterExpression anyIt = HandleLambdaParameters(anyNode.RangeVariables);
            FillFilterDetailProperties(anyNode);
            Expression source;
            Contract.Assert(anyNode.Source != null);
            source = Bind(anyNode.Source);

            Expression body = null;

            // uri parser places an Constant node with value true for empty any() body
            if (anyNode.Body != null && anyNode.Body.Kind != QueryNodeKind.Constant)
            {
                body = Bind(anyNode.Body);
                body = ApplyNullPropagationForFilterBody(body);
                body = Expression.Lambda(body, anyIt);
            }

            Expression any = Any(source, body);

            ExitLamdbaScope();

            if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True && IsNullable(source.Type))
            {
                // IFF(source == null) null; else Any(body);
                any = ToNullable(any);
                return Expression.Condition(
                    test: Expression.Equal(source, _nullConstant),
                    ifTrue: Expression.Constant(null, any.Type),
                    ifFalse: any);
            }
            else
            {
                return any;
            }
        }

        private ParameterExpression HandleLambdaParameters(IEnumerable<RangeVariable> rangeVariables)
        {
            ParameterExpression lambdaIt = null;

            EnterLambdaScope();

            Dictionary<string, ParameterExpression> newParameters = new Dictionary<string, ParameterExpression>();
            foreach (RangeVariable rangeVariable in rangeVariables)
            {
                ParameterExpression parameter;
                if (!_lambdaParameters.TryGetValue(rangeVariable.Name, out parameter))
                {
                    // Work-around issue 481323 where UriParser yields a collection parameter type
                    // for primitive collections rather than the inner element type of the collection.
                    // Remove this block of code when 481323 is resolved.
                    IEdmTypeReference edmTypeReference = rangeVariable.TypeReference;
                    IEdmCollectionTypeReference collectionTypeReference = edmTypeReference as IEdmCollectionTypeReference;
                    if (collectionTypeReference != null)
                    {
                        IEdmCollectionType collectionType = collectionTypeReference.Definition as IEdmCollectionType;
                        if (collectionType != null)
                        {
                            edmTypeReference = collectionType.ElementType;
                        }
                    }

                    parameter = Expression.Parameter(EdmLibHelpers.GetClrType(edmTypeReference, _model, _assembliesResolver), rangeVariable.Name);
                    Contract.Assert(lambdaIt == null, "There can be only one parameter in an Any/All lambda");
                    lambdaIt = parameter;
                }

                newParameters.Add(rangeVariable.Name, parameter);
            }

            _lambdaParameters = newParameters;
            return lambdaIt;
        }

        // If the expression is of non-standard edm primitive type (like uint), convert the expression to its standard edm type.
        // Also, note that only expressions generated for ushort, uint and ulong can be understood by linq2sql and EF.
        // The rest (char, char[], Binary) would cause issues with linq2sql and EF.
        private Expression ConvertNonStandardPrimitives(Expression source)
        {
            bool isNonstandardEdmPrimitive;
            if (source.Type.IsSubclassOf(typeof(ICSSoft.STORMNET.DetailArray)))
            {
                Type elementType = source.Type.GetProperty("Item").PropertyType;
                return Expression.Call(null, ExpressionHelperMethods.EnumerableCast.MakeGenericMethod(elementType), source);
            }

            Type conversionType = EdmLibHelpers.IsNonstandardEdmPrimitive(source.Type, out isNonstandardEdmPrimitive);

            if (isNonstandardEdmPrimitive)
            {
                Type sourceType = TypeHelper.GetUnderlyingTypeOrSelf(source.Type);

                Contract.Assert(sourceType != conversionType);

                Expression convertedExpression = null;

                if (sourceType.IsEnum)
                {
                    // we handle enum conversions ourselves
                    convertedExpression = source;
                }
                else
                {
                    switch (Type.GetTypeCode(sourceType))
                    {
                        case TypeCode.UInt16:
                        case TypeCode.UInt32:
                        case TypeCode.UInt64:
                            convertedExpression = Expression.Convert(ExtractValueFromNullableExpression(source), conversionType);
                            break;

                        case TypeCode.Char:
                            convertedExpression = Expression.Call(ExtractValueFromNullableExpression(source), "ToString", typeArguments: null, arguments: null);
                            break;

                        case TypeCode.DateTime:
                            convertedExpression = source;
                            break;

                        case TypeCode.Object:
                            if (sourceType == typeof(char[]))
                            {
                                convertedExpression = Expression.New(typeof(string).GetConstructor(new[] { typeof(char[]) }), source);
                            }
                            else if (sourceType == typeof(XElement))
                            {
                                convertedExpression = Expression.Call(source, "ToString", typeArguments: null, arguments: null);
                            }
                            //else if (sourceType == typeof((ICSSoft.STORMNET.UserDataTypes.Blob))
                            //{
                            //    convertedExpression = Expression.Call(source, "ToArray", typeArguments: null, arguments: null);
                            //}
                            else if (sourceType == typeof(ICSSoft.STORMNET.UserDataTypes.NullableDateTime)
                                || sourceType == typeof(ICSSoft.STORMNET.UserDataTypes.NullableDecimal)
                                || sourceType == typeof(ICSSoft.STORMNET.UserDataTypes.NullableInt))
                            {
                                convertedExpression = Expression.Property(source, "Value");
                            }

                            break;

                        default:
                            Contract.Assert(false, Error.Format("missing non-standard type support for {0}", sourceType.Name));
                            break;
                    }
                }

                if (_querySettings.HandleNullPropagation == HandleNullPropagationOption.True && IsNullable(source.Type))
                {
                    // source == null ? null : source
                    return Expression.Condition(
                        CheckForNull(source),
                        ifTrue: Expression.Constant(null, ToNullable(convertedExpression.Type)),
                        ifFalse: ToNullable(convertedExpression));
                }
                else
                {
                    return convertedExpression;
                }
            }

            return source;
        }

        private void EnterLambdaScope()
        {
            Contract.Assert(_lambdaParameters != null);
            _parametersStack.Push(_lambdaParameters);
        }

        private void ExitLamdbaScope()
        {
            if (_parametersStack.Count != 0)
            {
                _lambdaParameters = _parametersStack.Pop();
            }
            else
            {
                _lambdaParameters = null;
            }
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "These are simple conversion function and cannot be split up.")]
        private Expression CreateBinaryExpression(Microsoft.OData.UriParser.BinaryOperatorKind binaryOperator, Expression left, Expression right, bool liftToNull)
        {
            ExpressionType binaryExpressionType;

            // When comparing an enum to a string, parse the string, convert both to the enum underlying type, and compare the values
            // When comparing an enum to an enum with the same type, convert both to the underlying type, and compare the values
            Type leftUnderlyingType = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
            Type rightUnderlyingType = Nullable.GetUnderlyingType(right.Type) ?? right.Type;

            // Convert to integers unless Enum type is required
            if ((leftUnderlyingType.IsEnum || rightUnderlyingType.IsEnum) && binaryOperator != Microsoft.OData.UriParser.BinaryOperatorKind.Has)
            {
                left = BindCastToStringType(left);
                right = BindCastToStringType(right);
            }

            if (leftUnderlyingType == typeof(DateTime) && rightUnderlyingType == typeof(DateTimeOffset))
            {
                right = DateTimeOffsetToDateTime(right);
            }
            else if (rightUnderlyingType == typeof(DateTime) && leftUnderlyingType == typeof(DateTimeOffset))
            {
                left = DateTimeOffsetToDateTime(left);
            }

            if ((IsDateOrOffset(leftUnderlyingType) && IsDate(rightUnderlyingType)) ||
                (IsDate(leftUnderlyingType) && IsDateOrOffset(rightUnderlyingType)))
            {
                left = CreateDateBinaryExpression(left);
                right = CreateDateBinaryExpression(right);
            }

            if ((IsDateOrOffset(leftUnderlyingType) && IsTimeOfDay(rightUnderlyingType)) ||
                (IsTimeOfDay(leftUnderlyingType) && IsDateOrOffset(rightUnderlyingType)))
            {
                left = CreateTimeBinaryExpression(left);
                right = CreateTimeBinaryExpression(right);
            }

            if (left.Type != right.Type)
            {
                // one of them must be nullable and the other is not.
                left = ToNullable(left);
                right = ToNullable(right);
            }

            if (left.Type == typeof(string) || right.Type == typeof(string))
            {
                // convert nulls of type object to nulls of type string to make the String.Compare call work
                left = ConvertNull(left, typeof(string));
                right = ConvertNull(right, typeof(string));

                // Use string.Compare instead of comparison for gt, ge, lt, le between two strings since direct comparisons are not supported
                switch (binaryOperator)
                {
                    case Microsoft.OData.UriParser.BinaryOperatorKind.GreaterThan:
                    case Microsoft.OData.UriParser.BinaryOperatorKind.GreaterThanOrEqual:
                    case Microsoft.OData.UriParser.BinaryOperatorKind.LessThan:
                    case Microsoft.OData.UriParser.BinaryOperatorKind.LessThanOrEqual:
                        left = Expression.Call(_stringCompareMethodInfo, left, right, _ordinalStringComparisonConstant);
                        right = _zeroConstant;
                        break;
                    default:
                        break;
                }
            }

            if (_binaryOperatorMapping.TryGetValue(binaryOperator, out binaryExpressionType))
            {
                if (left.Type == typeof(byte[]) || right.Type == typeof(byte[]))
                {
                    left = ConvertNull(left, typeof(byte[]));
                    right = ConvertNull(right, typeof(byte[]));

                    switch (binaryExpressionType)
                    {
                        case ExpressionType.Equal:
                            return Expression.MakeBinary(binaryExpressionType, left, right, liftToNull, method: Linq2ObjectsComparisonMethods.AreByteArraysEqualMethodInfo);
                        case ExpressionType.NotEqual:
                            return Expression.MakeBinary(binaryExpressionType, left, right, liftToNull, method: Linq2ObjectsComparisonMethods.AreByteArraysNotEqualMethodInfo);
                        default:
                            Microsoft.OData.Edm.IEdmPrimitiveType binaryType = EdmLibHelpers.GetEdmPrimitiveTypeOrNull(typeof(byte[]));
                            throw new ODataException(Error.Format(SRResources.BinaryOperatorNotSupported, binaryType.Name, binaryType.Name, binaryOperator));
                    }
                }
                else
                {
                    return Expression.MakeBinary(binaryExpressionType, left, right, liftToNull, method: null);
                }
            }
            else
            {
                // Enum has a "has" operator
                // {(c1, c2) => c1.HasFlag(Convert(c2))}
                if (TypeHelper.IsEnum(left.Type) && TypeHelper.IsEnum(right.Type) && binaryOperator == BinaryOperatorKind.Has)
                {
                    UnaryExpression flag = Expression.Convert(right, typeof(Enum));
                    return BindHas(left, flag);
                }
                else
                {
                    throw Error.NotSupported(SRResources.QueryNodeBindingNotSupported, binaryOperator, typeof(FilterBinder).Name);
                }
            }
        }

        private Expression GetProperty(Expression source, string propertyName)
        {
            if (IsDateOrOffset(source.Type))
            {
                if (IsDateTime(source.Type))
                {
                    return MakePropertyAccess(ClrCanonicalFunctions.DateTimeProperties[propertyName], source);
                }
                else
                {
                    return MakePropertyAccess(ClrCanonicalFunctions.DateTimeOffsetProperties[propertyName], source);
                }
            }
            else if (IsDate(source.Type))
            {
                return MakePropertyAccess(ClrCanonicalFunctions.DateProperties[propertyName], source);
            }
            else if (IsTimeOfDay(source.Type))
            {
                return MakePropertyAccess(ClrCanonicalFunctions.TimeOfDayProperties[propertyName], source);
            }

            return source;
        }

        private Expression CreateDateBinaryExpression(Expression source)
        {
            // Year, Month, Day
            Expression year = GetProperty(source, ClrCanonicalFunctions.YearFunctionName);
            Expression month = GetProperty(source, ClrCanonicalFunctions.MonthFunctionName);
            Expression day = GetProperty(source, ClrCanonicalFunctions.DayFunctionName);

            // return (year * 10000 + month * 100 + day)
            Expression result =
                Expression.Add(
                    Expression.Add(
                        Expression.Multiply(year, Expression.Constant(10000)),
                        Expression.Multiply(month, Expression.Constant(100))), day);

            return CreateFunctionCallWithNullPropagation(result, new[] { source });
        }

        private Expression CreateTimeBinaryExpression(Expression source)
        {
            // Hour, Minute, Second, Millisecond
            Expression hour = GetProperty(source, ClrCanonicalFunctions.HourFunctionName);
            Expression minute = GetProperty(source, ClrCanonicalFunctions.MinuteFunctionName);
            Expression second = GetProperty(source, ClrCanonicalFunctions.SecondFunctionName);
            Expression milliSecond = GetProperty(source, ClrCanonicalFunctions.MillisecondFunctionName);

            Expression hourTicks = Expression.Multiply(Expression.Convert(hour, typeof(long)), Expression.Constant(Microsoft.OData.Edm.TimeOfDay.TicksPerHour));
            Expression minuteTicks = Expression.Multiply(Expression.Convert(minute, typeof(long)), Expression.Constant(Microsoft.OData.Edm.TimeOfDay.TicksPerMinute));
            Expression secondTicks = Expression.Multiply(Expression.Convert(second, typeof(long)), Expression.Constant(Microsoft.OData.Edm.TimeOfDay.TicksPerSecond));

            // return (hour * TicksPerHour + minute * TicksPerMinute + second * TicksPerSecond + millisecond)
            Expression result = Expression.Add(hourTicks, Expression.Add(minuteTicks, Expression.Add(secondTicks, Expression.Convert(milliSecond, typeof(long)))));

            return CreateFunctionCallWithNullPropagation(result, new[] { source });
        }
    }
}
