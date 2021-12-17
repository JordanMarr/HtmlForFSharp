﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace HtmlForFSharp
{
    #region Classifier

    internal class HtmlClassifier : IClassifier
    {
        private readonly IClassificationType _htmlDelimiterType;
        private readonly IClassificationType _htmlElementType;
        private readonly IClassificationType _htmlAttributeNameType;
        private readonly IClassificationType _htmlQuoteType;
        private readonly IClassificationType _htmlAttributeValueType;
        private readonly IClassificationType _htmlTextType;
        private readonly IClassificationType _htmlLitAttributeNameType;
        private readonly IClassificationType _htmlLitAttributeValueType;
        private readonly IClassifier _classifier;

        internal HtmlClassifier(IClassificationTypeRegistryService registry, IClassifier classifier)
        {
            _htmlDelimiterType = registry.GetClassificationType(FormatNames.Delimiter);
            _htmlElementType = registry.GetClassificationType(FormatNames.Element);
            _htmlAttributeNameType = registry.GetClassificationType(FormatNames.AttributeName);
            _htmlQuoteType = registry.GetClassificationType(FormatNames.Quote);
            _htmlAttributeValueType = registry.GetClassificationType(FormatNames.AttributeValue);
            _htmlTextType = registry.GetClassificationType(FormatNames.Text);
            _htmlLitAttributeNameType = registry.GetClassificationType(FormatNames.LitAttributeName);
            _htmlLitAttributeValueType = registry.GetClassificationType(FormatNames.LitAttributeValue);

            _classifier = classifier;
        }

        private static bool IsHtmlIdentifier(ClassificationSpan x) =>
                x.ClassificationType.Classification.ToLower() == "identifier"
                    && x.Span.GetText() == "html";

        private bool multiLineHtmlOpen = false;
        private int lastPosMultiLine = -1;

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            // if you change one line, in a multiline construct,
            // the next span is the beginning of the file
            if (lastPosMultiLine > span.Start)
                multiLineHtmlOpen = false;

            // Reset HTML State
            (currentStringType, state) =
                multiLineHtmlOpen
                ? (StringType.InterpolatedMultiLine, State.Default)
                : (StringType.Unknown, State.Default);

            var result = new List<ClassificationSpan>();
            var spanText = span.GetText();

            var spans = _classifier.GetClassificationSpans(span);
            if (!spans.Any(IsHtmlIdentifier) && !multiLineHtmlOpen)
                return result;

            var htmlIdentifierIdx =
                multiLineHtmlOpen
                ? 0
                : spans.IndexOf(spans.First(IsHtmlIdentifier));

            spans = spans.Skip(htmlIdentifierIdx).ToList();

            
            foreach (ClassificationSpan cs in spans)
            {
                var csClass = cs.ClassificationType.Classification.ToLower();

                // Only apply our rules if we found a string literal
                if (csClass == "string")
                {
                    if (cs.Span.Length > 2)
                    {
                        // the quotes are excluded in ScanLiteral
                        List<ClassificationSpan> classification = ScanLiteral(cs);

                        if (classification != null)
                        {
                            result.AddRange(classification);
                        }
                        else
                        {
                            result.Add(cs);
                        }
                    }
                    else
                    {
                        result.Add(cs);
                    }
                }
                else
                {
                    result.Add(cs);
                }
                

            }

            var idxOfMultilineStringSign = spanText.IndexOf("\"\"\"");

            if (multiLineHtmlOpen)
            {
                multiLineHtmlOpen = !spanText.Contains("\"\"\"");
                
            } else
            {
                multiLineHtmlOpen = spanText.Contains("\"\"\"")
                    && !spanText.Substring(idxOfMultilineStringSign + 1).Contains("\"\"\"");
                
            }

            lastPosMultiLine = multiLineHtmlOpen ? span.End : -1;


            return result;
        }

        private enum State
        {
            Default,
            AfterOpenAngleBracket,
            ElementName,
            InsideAttributeList,
            AttributeName,
            AfterAttributeName,
            AfterAttributeEqualSign,
            AfterOpenDoubleQuote,
            AfterOpenSingleQuote,
            AttributeValue,
            InsideElement,
            AfterCloseAngleBracket,
            AfterOpenTagSlash,
            AfterCloseTagSlash,
            LitAttributeName,
            //LitAttributeValue,
        }

        private bool IsNameChar(char c)
        {
            return c =='-' || c == '_' || char.IsLetterOrDigit(c);
        }

        //
        private State state = State.Default;
        private StringType currentStringType = StringType.Unknown;


        private enum HtmlIdentifierState {
            NoHtml,
            HtmlFound,
            NoHtmlButInsideInterpolation,
            HtmlInsideInterpolation
        }

        private enum StringType
        {
            Unknown,
            SimpleOrMultiline,
            InterpolatedSimple,
            InterpolatedMultiLine
        } 
        private List<ClassificationSpan> ScanLiteral(ClassificationSpan cs)
        {
            var span = cs.Span;
            var result = new List<ClassificationSpan>();

            var literal = span.GetText();

            
            // Classify StringType
            // and set state. On a "second part of an interpolation
            // remember last state!
            (currentStringType,state) =
                literal.StartsWith("$\"\"\"")
                ? (StringType.InterpolatedMultiLine, State.Default)
                : literal.StartsWith("$\"")
                    ? (StringType.InterpolatedSimple, State.Default)
                    : literal.StartsWith("\"\"\"") || literal.StartsWith("@\"") || literal.StartsWith("\"")
                        ? (StringType.SimpleOrMultiline, State.Default)
                        // next part of interpolated string
                        : literal.StartsWith("}")
                            ? (currentStringType,state)
                            // in case of a next line in a multiline string, continue with the last state
                            : currentStringType == StringType.InterpolatedMultiLine && !literal.StartsWith("\"\"\"")
                                ? (currentStringType, state)
                                : (StringType.Unknown, State.Default);


            var currentCharIndex = 0;

            int? continuousMark = null;
            var insideSingleQuote = false;
            var insideDoubleQuote = false;

            while (currentCharIndex < literal.Length)
            {
                var c = literal[currentCharIndex];
                var prevChar = currentCharIndex > 0 ? literal[currentCharIndex - 1] : '\0';

                //check is quote of start and end
                if ((currentCharIndex == 0 || currentCharIndex == (literal.Length-1))  && IsQuote(c))
                {
                    currentCharIndex++;
                    continue;
                }

                if ((c=='{' || c=='}') && state != State.LitAttributeName)
                {
                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), cs.ClassificationType));
                    currentCharIndex++;
                    continue;
                }

                
                switch (state)
                {
                    case State.Default:
                        {
                            if (c == '<')
                            {
                                state = State.AfterOpenAngleBracket;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            break;
                        }
                    case State.AfterOpenAngleBracket:
                        {
                            if (IsNameChar(c))
                            {
                                continuousMark = currentCharIndex;
                                state = State.ElementName;
                            }
                            else if (c == '/')
                            {
                                state = State.AfterCloseTagSlash;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.ElementName:
                        {
                            if (IsNameChar(c))
                            {

                            }
                            else if (char.IsWhiteSpace(c))
                            {
                                if (continuousMark.HasValue)
                                {
                                    var length = currentCharIndex - continuousMark.Value;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + continuousMark.Value, length), _htmlElementType));
                                    continuousMark = null;
                                }
                                state = State.InsideAttributeList;
                            }
                            else if (c == '>')
                            {
                                if (continuousMark.HasValue)
                                {
                                    var length = currentCharIndex - continuousMark.Value;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + continuousMark.Value, length), _htmlElementType));
                                    continuousMark = null;
                                }

                                state = State.AfterCloseAngleBracket;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else if (c == '/')
                            {
                                if (continuousMark.HasValue)
                                {
                                    var length = currentCharIndex - continuousMark.Value;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + continuousMark.Value, length), _htmlElementType));
                                    continuousMark = null;
                                }

                                state = State.AfterOpenTagSlash;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.InsideAttributeList:
                        {
                            if (char.IsWhiteSpace(c))
                            {

                            }
                            else if (IsNameChar(c))
                            {
                                continuousMark = currentCharIndex;
                                state = State.AttributeName;
                            }
                            else if (c =='.' || c == '@')
                            {
                                state = State.LitAttributeName;
                                continuousMark = currentCharIndex;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlLitAttributeNameType));
                            }
                            else if (c == '>')
                            {
                                state = State.AfterCloseAngleBracket;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else if (c == '/')
                            {
                                state = State.AfterOpenTagSlash;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.AttributeName:
                        {
                            if (char.IsWhiteSpace(c))
                            {
                                if (continuousMark.HasValue)
                                {
                                    var length = currentCharIndex - continuousMark.Value;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + continuousMark.Value, length), _htmlAttributeNameType));
                                    continuousMark = null;
                                }
                                state = State.AfterAttributeName;
                            }
                            else if (IsNameChar(c))
                            {

                            }
                            else if (c == '=')
                            {
                                if (continuousMark.HasValue)
                                {
                                    var attrNameStart = continuousMark.Value;
                                    var attrNameLength = currentCharIndex - attrNameStart;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + attrNameStart, attrNameLength), _htmlAttributeNameType));
                                }

                                state = State.AfterAttributeEqualSign;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else if (c == '>')
                            {
                                if (continuousMark.HasValue)
                                {
                                    var attrNameStart = continuousMark.Value;
                                    var attrNameLength = currentCharIndex - attrNameStart;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + attrNameStart, attrNameLength), _htmlAttributeNameType));
                                }

                                state = State.AfterCloseAngleBracket;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else if (c == '/')
                            {
                                if (continuousMark.HasValue)
                                {
                                    var attrNameStart = continuousMark.Value;
                                    var attrNameLength = currentCharIndex - attrNameStart;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + attrNameStart, attrNameLength), _htmlAttributeNameType));
                                }

                                state = State.AfterOpenTagSlash;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.LitAttributeName:
                        {
                            if (char.IsWhiteSpace(c))
                            {
                                if (continuousMark.HasValue)
                                {
                                    var length = currentCharIndex - continuousMark.Value;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + continuousMark.Value, length), _htmlLitAttributeNameType));
                                    continuousMark = null;
                                }
                                state = State.LitAttributeName;
                            }
                            else if (IsNameChar(c))
                            {
                                state = State.LitAttributeName;
                            }
                            else if (c == '=')
                            {
                                if (continuousMark.HasValue)
                                {
                                    var attrNameStart = continuousMark.Value;
                                    var attrNameLength = currentCharIndex - attrNameStart;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + attrNameStart, attrNameLength), _htmlLitAttributeNameType));
                                }
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlLitAttributeNameType));
                            }
                            else if (c == '{')
                            {
                                if (continuousMark.HasValue)
                                {
                                    var attrNameStart = continuousMark.Value;
                                    var attrNameLength = currentCharIndex - attrNameStart;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + attrNameStart, attrNameLength), _htmlLitAttributeValueType));
                                }
                                state = State.LitAttributeName;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlLitAttributeValueType));
                            }
                            else if (c == '}')
                            {
                                state = State.InsideAttributeList;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlLitAttributeValueType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.AfterAttributeName:
                        {
                            if (char.IsWhiteSpace(c))
                            {

                            }
                            else if (IsNameChar(c))
                            {
                                continuousMark = currentCharIndex;
                                state = State.AttributeName;
                            }
                            else if (c == '=')
                            {
                                state = State.AfterAttributeEqualSign;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else if (c == '/')
                            {
                                state = State.AfterOpenTagSlash;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else if (c == '>')
                            {
                                state = State.AfterCloseAngleBracket;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.AfterAttributeEqualSign:
                        {
                            if (char.IsWhiteSpace(c))
                            {

                            }
                            else if (IsNameChar(c))
                            {
                                continuousMark = currentCharIndex;
                                state = State.AttributeValue;
                            }
                            else if (c == '\"' && !literal.StartsWith("@"))
                            {
                                state = State.AfterOpenDoubleQuote;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else if (c == '\\' && literal.StartsWith("\""))
                            {
                                state = State.AfterAttributeEqualSign;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else if (c == '\"' && literal.StartsWith("\"") && prevChar == '\\')
                            {
                                state = State.AfterOpenDoubleQuote;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else if (c == '\"' && literal.StartsWith("@") && prevChar != '\"')
                            {
                                state = State.AfterAttributeEqualSign;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else if (c == '\"' && literal.StartsWith("@") && prevChar == '\"')
                            {
                                state = State.AfterOpenDoubleQuote;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else if (c == '\"' && literal.StartsWith("\"") && prevChar == '\\')
                            {
                                state = State.AfterOpenDoubleQuote;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else if (c == '\'')
                            {
                                state = State.AfterOpenSingleQuote;
                                insideSingleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.AfterOpenDoubleQuote:
                        {
                            if (c == '\"' && literal.StartsWith("@") && prevChar != '\"')
                            {
                                state = State.AfterOpenDoubleQuote;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            if (c == '\"' && literal.StartsWith("@") && prevChar == '\"')
                            {
                                state = State.InsideAttributeList;
                                insideDoubleQuote = false;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            if (c == '\"')
                            {
                                state = State.InsideAttributeList;
                                insideDoubleQuote = false;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else
                            {
                                continuousMark = currentCharIndex;
                                state = State.AttributeValue;
                            }
                            break;
                        }
                    case State.AfterOpenSingleQuote:
                        {
                            if (c == '\'')
                            {
                                state = State.InsideAttributeList;
                                insideSingleQuote = false;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else
                            {
                                continuousMark = currentCharIndex;
                                state = State.AttributeValue;
                            }
                            break;
                        }
                    case State.AttributeValue:
                        {
                            if (c == '\'')
                            {
                                if (insideSingleQuote)
                                {
                                    state = State.InsideAttributeList;
                                    insideSingleQuote = false;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));

                                    if (continuousMark.HasValue)
                                    {
                                        var start = continuousMark.Value;
                                        var length = currentCharIndex - start;
                                        continuousMark = null;

                                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), _htmlAttributeValueType));
                                    }
                                }
                            }
                            if (c == '\"' && literal.StartsWith("@") && prevChar != '\"')
                            {
                                state = State.AttributeValue;
                                insideDoubleQuote = true;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));
                            }
                            else if (c == '\"' && literal.StartsWith("@") && prevChar == '\"')
                            {
                                state = State.InsideAttributeList;
                                insideDoubleQuote = false;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));

                                if (continuousMark.HasValue)
                                {
                                    var start = continuousMark.Value;
                                    var length = currentCharIndex - start;
                                    continuousMark = null;

                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), _htmlAttributeValueType));
                                }
                            }
                            else if (c == '\"')
                            {
                                if (insideDoubleQuote)
                                {
                                    state = State.InsideAttributeList;
                                    insideDoubleQuote = false;
                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlQuoteType));

                                    if (continuousMark.HasValue)
                                    {
                                        var start = continuousMark.Value;
                                        var length = currentCharIndex - start;
                                        continuousMark = null;

                                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), _htmlAttributeValueType));
                                    }
                                }
                            }
                            else
                            {

                            }

                            break;
                        }


                    


                    case State.AfterCloseAngleBracket:
                        {
                            if (c == '<')
                            {
                                state = State.AfterOpenAngleBracket;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else
                            {
                                continuousMark = currentCharIndex;
                                state = State.InsideElement;
                            }
                            break;
                        }
                    case State.InsideElement:
                        {
                            if (c == '<')
                            {
                                state = State.AfterOpenAngleBracket;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));

                                if (continuousMark.HasValue)
                                {
                                    var start = continuousMark.Value;
                                    var length = currentCharIndex - start;
                                    continuousMark = null;

                                    result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), cs.ClassificationType));
                                }
                            }
                            else
                            {

                            }

                            break;
                        }
                    case State.AfterCloseTagSlash:
                        {
                            if (char.IsWhiteSpace(c))
                            {

                            }
                            else if (IsNameChar(c))
                            {
                                continuousMark = currentCharIndex;
                                state = State.ElementName;
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    case State.AfterOpenTagSlash:
                        {
                            if (c == '>')
                            {
                                state = State.AfterCloseAngleBracket;
                                continuousMark = null;
                                result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + currentCharIndex, 1), _htmlDelimiterType));
                            }
                            else
                            {
                                return null;
                            }
                            break;
                        }
                    default:
                        break;
                }

                ++currentCharIndex;
            }

            // if the continuous span is stopped because of end of literal,
            // the span was not colored, handle it here
            if (currentCharIndex >= literal.Length)
            {
                if (continuousMark.HasValue)
                {
                    if (state == State.ElementName)
                    {
                        var start = continuousMark.Value;
                        var length = literal.Length - start;
                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), _htmlElementType));
                    }
                    else if (state == State.AttributeName)
                    {
                        var attrNameStart = continuousMark.Value;
                        var attrNameLength = literal.Length - attrNameStart;
                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + attrNameStart, attrNameLength), _htmlAttributeNameType));
                    }
                    else if (state == State.AttributeValue)
                    {
                        var start = continuousMark.Value;
                        var length = literal.Length - start;
                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), _htmlAttributeValueType));
                    }
                    else if (state == State.LitAttributeName)
                    {
                        var start = continuousMark.Value;
                        var length = literal.Length - start;
                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), _htmlLitAttributeNameType));
                    }
                    else if (state == State.InsideElement)
                    {
                        var start = continuousMark.Value;
                        var length = literal.Length - start;
                        result.Add(new ClassificationSpan(new SnapshotSpan(span.Start + start, length), cs.ClassificationType));
                    }
                }
            }


            // Reset State on end of strings
            (currentStringType, state) =
                literal.Trim().EndsWith("\"\"\"")
                ? (StringType.Unknown, State.Default)
                : literal.Trim().EndsWith("\"")
                    ? (StringType.Unknown, State.Default)
                    : (currentStringType, state);

            return result;
        }
        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        private bool IsQuote(char c)
        {
            return c == '\'' || c == '"' || c == '`';
        }
    }
    #endregion //Classifier
}