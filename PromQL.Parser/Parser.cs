using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace PromQL.Parser
{
    public static class Parser
    {
        public static TokenListParser<PromToken, UnaryExpr> UnaryExpr =
            from op in Parse.Ref(() => UnaryOperator)
            from expr in Parse.Ref(() => Expr)
            select new UnaryExpr(op, expr);
        
        public static TokenListParser<PromToken, Operators.Unary> UnaryOperator =
            Token.EqualTo(PromToken.ADD).Select(_ => Operators.Unary.Add).Or(
                Token.EqualTo(PromToken.SUB).Select(_ => Operators.Unary.Sub)
            );

        public static TokenListParser<PromToken, MetricIdentifier> MetricIdentifier =
            from id in Token.EqualTo(PromToken.METRIC_IDENTIFIER)
                // TODO support all function names and keywords here + dedupe logic from LabelIdentifier
                .Or(Token.EqualTo(PromToken.IDENTIFIER))
                .Or(Token.Matching<PromToken>(t => AggregateOperatorMap.ContainsKey(t), "aggregate_op"))
            select new MetricIdentifier(id.ToStringValue());

        public static TokenListParser<PromToken, LabelMatchers> LabelMatchers =
            from lb in Token.EqualTo(PromToken.LEFT_BRACE)
            from matchers in (
                from matcherHead in LabelMatcher
                from matcherTail in (
                    from c in Token.EqualTo(PromToken.COMMA)
                    from m in LabelMatcher
                    select m
                ).Try().Many()
                from comma in Token.EqualTo(PromToken.COMMA).Optional()
                select new [] { matcherHead }.Concat(matcherTail)
            ).OptionalOrDefault(Array.Empty<LabelMatcher>())
            from rb in Token.EqualTo(PromToken.RIGHT_BRACE)
            select new LabelMatchers(matchers.ToImmutableArray());
        
        public static TokenListParser<PromToken, VectorSelector> VectorSelector =
        (
            from m in MetricIdentifier
            from lm in LabelMatchers.OptionalOrDefault()
            select new VectorSelector(m, lm)
        ).Or(
            from lm in LabelMatchers
            select new VectorSelector(lm)
        );

        public static TokenListParser<PromToken, MatrixSelector> MatrixSelector =
            from vs in VectorSelector
            from d in Duration.Between(Token.EqualTo(PromToken.LEFT_BRACKET), Token.EqualTo(PromToken.RIGHT_BRACKET))
            select new MatrixSelector(vs, d);

        // TODO see https://github.com/prometheus/prometheus/blob/7471208b5c8ff6b65b644adedf7eb964da3d50ae/promql/parser/generated_parser.y#L679
        public static TokenListParser<PromToken, string> LabelValueMatcher =
            from id in Token.EqualTo(PromToken.IDENTIFIER)
                .Or(Token.Matching<PromToken>(t => AggregateOperatorMap.ContainsKey(t), "agg_label_op"))
                // TODO must expand these
                .Or(Token.EqualTo(PromToken.IGNORING))
                .Or(Token.EqualTo(PromToken.ON))
                .Or(Token.EqualTo(PromToken.OFFSET))
            select id.ToStringValue();
        
        public static TokenListParser<PromToken, LabelMatcher> LabelMatcher =
            from id in LabelValueMatcher
            from op in MatchOp
            from str in String
            select new LabelMatcher(id, op, (StringLiteral)str);
        
        public static TokenListParser<PromToken, Operators.Match> MatchOp =
            Token.EqualTo(PromToken.EQL).Select(_ => Operators.Match.Equal)
                .Or(
                    Token.EqualTo(PromToken.NEQ).Select(_ => Operators.Match.NotEqual)
                ).Or(
                    Token.EqualTo(PromToken.EQL_REGEX).Select(_ => Operators.Match.Regexp)
                ).Or(
                    Token.EqualTo(PromToken.NEQ_REGEX).Select(_ => Operators.Match.NotRegexp)
                );

        public static TokenListParser<PromToken, NumberLiteral> Number =
            from s in (
                Token.EqualTo(PromToken.ADD).Or(Token.EqualTo(PromToken.SUB))
            ).OptionalOrDefault(new Token<PromToken>(PromToken.ADD, TextSpan.Empty))
            from n in Token.EqualTo(PromToken.NUMBER)
            select new NumberLiteral(double.Parse(n.ToStringValue()) * (s.Kind == PromToken.SUB ? -1 : 1));

        /// <summary>
        /// Taken from https://github.com/prometheus/common/blob/88f1636b699ae4fb949d292ffb904c205bf542c9/model/time.go#L186
        /// </summary>
        /// <returns></returns>
        public static Regex DurationRegex =
            new Regex("^(([0-9]+)y)?(([0-9]+)w)?(([0-9]+)d)?(([0-9]+)h)?(([0-9]+)m)?(([0-9]+)s)?(([0-9]+)ms)?$",
                RegexOptions.Compiled);

        public static TokenListParser<PromToken, Duration> Duration =
            Token.EqualTo(PromToken.DURATION)
                .Select(n =>
                {
                    static TimeSpan ParseComponent(Match m, int index, Func<int, TimeSpan> parser)
                    {
                        if (m.Groups[index].Success)
                            return parser(int.Parse(m.Groups[index].Value));

                        return TimeSpan.Zero;
                    }

                    var match = DurationRegex.Match(n.ToStringValue());
                    if (!match.Success)
                        throw new ParseException($"Invalid duration: {n.ToStringValue()}", n.Position);

                    var ts = TimeSpan.Zero;
                    ts += ParseComponent(match, 2, i => TimeSpan.FromDays(i) * 365);
                    ts += ParseComponent(match, 4, i => TimeSpan.FromDays(i) * 7);
                    ts += ParseComponent(match, 6, i => TimeSpan.FromDays(i));
                    ts += ParseComponent(match, 8, i => TimeSpan.FromHours(i));
                    ts += ParseComponent(match, 10, i => TimeSpan.FromMinutes(i));
                    ts += ParseComponent(match, 12, i => TimeSpan.FromSeconds(i));
                    ts += ParseComponent(match, 14, i => TimeSpan.FromMilliseconds(i));

                    return new Duration(ts);
                });

        public static TokenListParser<PromToken, StringLiteral> String =
            Token.EqualTo(PromToken.STRING)
                .Select(n => new StringLiteral(n.Span.ConsumeChar().Value, n.Span.ToStringValue()[1..^1]));


        public static Func<Expr, TokenListParser<PromToken, OffsetExpr>> OffsetExpr = (Expr expr) =>
            from offset in Token.EqualTo(PromToken.OFFSET)
            from neg in Token.EqualTo(PromToken.SUB).Optional()
            from duration in Duration
            select new OffsetExpr(expr, new Duration(new TimeSpan(duration.Value.Ticks * (neg.HasValue ? -1 : 1))));

        public static TokenListParser<PromToken, ParenExpression> ParenExpression =
            from e in Parse.Ref(() => Expr.Between(Token.EqualTo(PromToken.LEFT_PAREN), Token.EqualTo(PromToken.RIGHT_PAREN)))
            select new ParenExpression(e);

        private static FunctionIdentifier? LookupFunctionName(string fnName)
        {
            var camelCase = Regex.Replace(fnName, "([a-z])_([a-z])", (m) =>
            {
                return m.Groups[1].Value + m.Groups[2].Value.ToUpper();
            });
            var titleCase = char.ToUpper(camelCase.First()) + camelCase[1..];
            
            if (Enum.TryParse<FunctionIdentifier>(titleCase, out var fnId))
                return fnId;

            return null;
        }
        
        public static TokenListParser<PromToken, Expr[]> FunctionArgs = Parse.Ref(() => Expr.ManyDelimitedBy(Token.EqualTo(PromToken.COMMA)))
            .Between(Token.EqualTo(PromToken.LEFT_PAREN), Token.EqualTo(PromToken.RIGHT_PAREN));
        
        public static TokenListParser<PromToken, FunctionCall> FunctionCall =
            from id in Token.EqualTo(PromToken.IDENTIFIER)
                .Select(x => LookupFunctionName(x.ToStringValue()))
                .Where(id => id != null,  $"Unrecognized function name")
            from args in FunctionArgs
            select new FunctionCall(id.Value, args.ToImmutableArray());


        public static TokenListParser<PromToken, ImmutableArray<string>> GroupingLabels =
            from labels in (LabelValueMatcher.ManyDelimitedBy(Token.EqualTo(PromToken.COMMA)))
                .Between(Token.EqualTo(PromToken.LEFT_PAREN), Token.EqualTo(PromToken.RIGHT_PAREN))
            select labels.Select(x => x).ToImmutableArray();

        public static TokenListParser<PromToken, bool> BoolModifier =
            from b in Token.EqualTo(PromToken.BOOL).Optional()
            select b.HasValue;
        
        public static TokenListParser<PromToken, VectorMatching> OnOrIgnoring =
            from b in BoolModifier
            from onOrIgnoring in Token.EqualTo(PromToken.ON).Or(Token.EqualTo(PromToken.IGNORING))
            from onOrIgnoringLabels in GroupingLabels
            select new VectorMatching(
                VectorMatchCardinality.OneToOne,
                onOrIgnoringLabels,
                onOrIgnoring.HasValue && onOrIgnoring.Kind == PromToken.ON,
                ImmutableArray<string>.Empty,
                b
            );

        public static Func<Expr, TokenListParser<PromToken, SubqueryExpr>> SubqueryExpr = (Expr expr) =>
            from lb in Token.EqualTo(PromToken.LEFT_BRACKET)
            from range in Duration
            from colon in Token.EqualTo(PromToken.COLON)
            from step in Duration.OptionalOrDefault()
            from rb in Token.EqualTo(PromToken.RIGHT_BRACKET)
            select new SubqueryExpr(expr, range, step);

        public static TokenListParser<PromToken, VectorMatching> VectorMatching =
            from vectMatching in (
                from vm in OnOrIgnoring
                from grp in Token.EqualTo(PromToken.GROUP_LEFT).Or(Token.EqualTo(PromToken.GROUP_RIGHT))
                from grpLabels in GroupingLabels.OptionalOrDefault(ImmutableArray<string>.Empty)
                select vm with
                {
                    MatchCardinality = grp switch
                    {
                        {HasValue : false} => VectorMatchCardinality.OneToOne,
                        {Kind: PromToken.GROUP_LEFT} => VectorMatchCardinality.ManyToOne,
                        {Kind: PromToken.GROUP_RIGHT} => VectorMatchCardinality.OneToMany,
                        _ => VectorMatchCardinality.OneToOne
                    },
                    MatchingLabels = grpLabels
                }
            ).Try().Or(
                from vm in OnOrIgnoring
                select vm
            ).Try().Or(
                from b in BoolModifier
                select new VectorMatching(b)
            )
            select vectMatching;

        private static IReadOnlyDictionary<PromToken, Operators.Binary> BinaryOperatorMap = new Dictionary<PromToken, Operators.Binary>()
        {
            [PromToken.ADD] = Operators.Binary.Add,
            [PromToken.LAND] = Operators.Binary.And,
            [PromToken.ATAN2] = Operators.Binary.Atan2,
            [PromToken.DIV] = Operators.Binary.Div,
            [PromToken.EQLC] = Operators.Binary.Eql,
            [PromToken.GTE] = Operators.Binary.Gte,
            [PromToken.GTR] = Operators.Binary.Gtr,
            [PromToken.LSS] = Operators.Binary.Lss,
            [PromToken.LTE] = Operators.Binary.Lte,
            [PromToken.MOD] = Operators.Binary.Mod,
            [PromToken.MUL] = Operators.Binary.Mul,
            [PromToken.NEQ] = Operators.Binary.Neq,
            [PromToken.LOR] = Operators.Binary.Or,
            [PromToken.POW] = Operators.Binary.Pow,
            [PromToken.SUB] = Operators.Binary.Sub,
            [PromToken.LUNLESS] = Operators.Binary.Unless
        };
        
        public static TokenListParser<PromToken, BinaryExpr> BinaryExpr =
            from lhs in Parse.Ref(() => ExprNotBinary)
            from op in Token.Matching<PromToken>(x => BinaryOperatorMap.ContainsKey(x), "binary_op")
            from vm in VectorMatching.OptionalOrDefault(new VectorMatching())
            from rhs in Expr
            select new BinaryExpr(lhs, rhs, BinaryOperatorMap[op.Kind], vm);
        
        private static IReadOnlyDictionary<PromToken, Operators.Aggregate> AggregateOperatorMap = new Dictionary<PromToken, Operators.Aggregate>()
        {
            [PromToken.AVG] = Operators.Aggregate.Avg,
            [PromToken.BOTTOMK] = Operators.Aggregate.Bottomk,
            [PromToken.COUNT] = Operators.Aggregate.Count,
            [PromToken.COUNT_VALUES] = Operators.Aggregate.CountValues,
            [PromToken.GROUP] = Operators.Aggregate.Group,
            [PromToken.MAX] = Operators.Aggregate.Max,
            [PromToken.MIN] = Operators.Aggregate.Min,
            [PromToken.QUANTILE] = Operators.Aggregate.Quantile,
            [PromToken.STDDEV] = Operators.Aggregate.Stddev,
            [PromToken.STDVAR] = Operators.Aggregate.Stdvar,
            [PromToken.SUM] = Operators.Aggregate.Sum,
            [PromToken.TOPK] = Operators.Aggregate.Topk
        };

        public static TokenListParser<PromToken, (bool without, ImmutableArray<string> labels)> AggregateModifier =
            from kind in Token.EqualTo(PromToken.BY)
                .Or(Token.EqualTo(PromToken.WITHOUT))
            from labels in GroupingLabels
            select (kind.Kind == PromToken.WITHOUT, labels);

        public static TokenListParser<PromToken, AggregateExpr> AggregateExpr =
            from op in Token.Matching<PromToken>(t => AggregateOperatorMap.ContainsKey(t), "aggregate_op")
            from argsAndMod in (
                from args in FunctionArgs
                from mod in AggregateModifier.OptionalOrDefault((without: false, labels: ImmutableArray<string>.Empty))
                select (mod, args)
            ).Or(
                from mod in AggregateModifier
                from args in FunctionArgs
                select (mod, args)
            )
            .Where(x => x.args.Length >= 1, "At least one argument is required for aggregate expressions")
            .Where(x => x.args.Length <= 2, "A maximum of two arguments is supported for aggregate expressions")
            select new AggregateExpr(AggregateOperatorMap[op.Kind], argsAndMod.args.Length > 1 ? argsAndMod.args[1] : argsAndMod.args[0], argsAndMod.args.Length > 1 ? argsAndMod.args[0] : null, argsAndMod.mod.labels, argsAndMod.mod.without );

        public static TokenListParser<PromToken, Expr> ExprNotBinary =
             from head in OneOf(
                 // TODO can we optimize order here?
                 Parse.Ref(() => ParenExpression).Cast<PromToken, ParenExpression, Expr>(),
                 Parse.Ref(() => AggregateExpr).Cast<PromToken, AggregateExpr, Expr>(),
                 Parse.Ref(() => FunctionCall).Cast<PromToken, FunctionCall, Expr>(),
                 Parse.Ref(() => UnaryExpr).Cast<PromToken, UnaryExpr, Expr>(),
                 MatrixSelector.Cast<PromToken, MatrixSelector, Expr>().Try(),
                 VectorSelector.Cast<PromToken, VectorSelector, Expr>(),
                 String.Cast<PromToken, StringLiteral, Expr>(),
                 Number.Cast<PromToken, NumberLiteral, Expr>()
             )
             from offsetOrSubquery in OffsetOrSubquery(head).OptionalOrDefault()
             select offsetOrSubquery ?? head;

        public static Func<Expr, TokenListParser<PromToken, Expr>> OffsetOrSubquery = (Expr expr) =>
             from offsetOfSubquery in (
                 from offset in OffsetExpr(expr)
                 select (Expr)offset
             ).Or(
                 from subquery in SubqueryExpr(expr)
                 select (Expr)subquery
             )
             select offsetOfSubquery;

         public static TokenListParser<PromToken, Expr> Expr =
             from head in Parse.Ref(() => BinaryExpr).Cast<PromToken, BinaryExpr, Expr>().Try().Or(ExprNotBinary)
             // TODO OR together or allow separately? What's the precendence here? Offsetable subquery or subqueried offset?
             // from offset
             // from subquery
             // from offset + subquery
             // from subquery + offset
             from offsetOrSubquery in OffsetOrSubquery(head).OptionalOrDefault()
             select offsetOrSubquery ?? head;

        /// <summary>
        /// Parse the specified input as a PromQL expression.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Expr ParseExpression(string input)
        {
            var t = new Tokenizer();
            return Expr.Parse(new TokenList<PromToken>(
                t.Tokenize(input).Where(x => x.Kind != PromToken.COMMENT).ToArray()
            ));
        }
        
        private static TokenListParser<PromToken, T> OneOf<T>(params TokenListParser<PromToken, T>[] parsers)
        {
            TokenListParser<PromToken, T> expr = parsers[0].Try();

            foreach (var p in parsers.Skip(1))
            {
                expr = expr.Or(p);
            }

            return expr;
        }
    }
}
