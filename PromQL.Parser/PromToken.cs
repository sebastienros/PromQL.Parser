using Superpower.Display;

namespace PromQL.Parser
{
	/// <summary>
	/// Lexical tokens that comprise a PromQL expression. 
	/// </summary>
	/// <remarks>
	/// Tokens are taken from https://github.com/prometheus/prometheus/blob/7471208b5c8ff6b65b644adedf7eb964da3d50ae/promql/parser/generated_parser.y#L43-L135
	/// Name casing is preserved (even though they violate the general C# style guidelines) 
	/// </remarks>
	public enum PromToken
	{
		None = 0,

		[Token(Example = "=")] EQL,
		[Token(Example = ":")] COLON,
		[Token(Example = ",")] COMMA,
		[Token] COMMENT,
		[Token] DURATION,

		// TODO Don't currentl use error, could be useful for more informative error messages?
		[Token] ERROR,

		[Token] IDENTIFIER,
		[Token(Example = "{")] LEFT_BRACE,
		[Token(Example = "[")] LEFT_BRACKET,
		[Token(Example = "(")] LEFT_PAREN,
		[Token] METRIC_IDENTIFIER,
		[Token] NUMBER,
		[Token(Example = "}")] RIGHT_BRACE,
		[Token(Example = "]")] RIGHT_BRACKET,
		[Token(Example = ")")] RIGHT_PAREN,
		[Token(Example = ";")] SEMICOLON,
		[Token] STRING,
		[Token] TIMES,

		// Operators
		[Token(Category = "Operators", Example = "+")]
		ADD,

		[Token(Category = "Operators", Example = "/")]
		DIV,

		[Token(Category = "Operators", Example = "==")]
		EQLC,

		[Token(Category = "Operators", Example = "=~")]
		EQL_REGEX,

		[Token(Category = "Operators", Example = ">=")]
		GTE,

		[Token(Category = "Operators", Example = ">")]
		GTR,

		[Token(Category = "Operators")]
		LAND,

		[Token(Category = "Operators")]
		LOR,

		[Token(Category = "Operators", Example = "<")]
		LSS,

		[Token(Category = "Operators", Example = "<=")]
		LTE,

		[Token(Category = "Operators")]
		LUNLESS,

		[Token(Category = "Operators", Example = "%")]
		MOD,

		[Token(Category = "Operators", Example = "*")]
		MUL,

		[Token(Category = "Operators", Example = "!=")]
		NEQ,

		[Token(Category = "Operators", Example = "!~")]
		NEQ_REGEX,

		[Token(Category = "Operators", Example = "^")]
		POW,

		[Token(Category = "Operators", Example = "-")]
		SUB,

		[Token(Category = "Operators", Example = "@")]
		AT,

		[Token(Category = "Operators", Example = "atan2")]
		ATAN2,

		// Aggregators
		AVG,

		[Token(Category = "Aggregators")]
		BOTTOMK,

		[Token(Category = "Aggregators")]
		COUNT,

		[Token(Category = "Aggregators")]
		COUNT_VALUES,

		[Token(Category = "Aggregators")]
		GROUP,

		[Token(Category = "Aggregators")]
		MAX,

		[Token(Category = "Aggregators")]
		MIN,

		[Token(Category = "Aggregators")]
		QUANTILE,

		[Token(Category = "Aggregators")]
		STDDEV,

		[Token(Category = "Aggregators")]
		STDVAR,

		[Token(Category = "Aggregators")]
		SUM,

		[Token(Category = "Aggregators")]
		TOPK,

		// Keywords
		BOOL,

		[Token(Category = "Keywords")]
		BY,

		[Token(Category = "Keywords")]
		GROUP_LEFT,

		[Token(Category = "Keywords")]
		GROUP_RIGHT,

		[Token(Category = "Keywords")]
		IGNORING,

		[Token(Category = "Keywords")]
		OFFSET,

		[Token(Category = "Keywords")]
		ON,

		[Token(Category = "Keywords")]
		WITHOUT,

		// Preprocessors
	}
}
