// <copyright file="HtmlHelperExtensions.cs" company="Real Good Apps">
// Copyright (c) Real Good Apps. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RealGoodApps.HtmlHelperFormat
{
    /// <summary>
    /// Extension methods for <see cref="IHtmlHelper"/>.
    /// </summary>
    public static class HtmlHelperExtensions
    {
        /// <summary>
        /// Format a string that contains a mix of pre-encoding HTML content and content that should be encoded safely.
        /// </summary>
        /// <param name="helper">An instance of <see cref="IHtmlHelper"/>.</param>
        /// <param name="format">The format string.</param>
        /// <param name="arguments">The arguments which can contain instances of <see cref="HtmlString"/>.</param>
        /// <returns>An instance of <see cref="IHtmlContent"/>.</returns>
        public static IHtmlContent Format(
            this IHtmlHelper helper,
            string format,
            params object[] arguments)
        {
            if (arguments.Length < 1)
            {
                var rawHtml = EncodeAndPreserveLineBreaks(
                    helper,
                    format);

                return new HtmlString(rawHtml);
            }

            var argumentsWrapped = WrapArguments(arguments);

            var probedString = ProbeFormatString(
                format,
                argumentsWrapped,
                out var argumentReferences);

            var regexPattern = BuildRegexPattern(argumentReferences);

            var matches = Regex.Matches(probedString, regexPattern);

            if (matches.Count == 0)
            {
                var rawHtml = EncodeAndPreserveLineBreaks(
                    helper,
                    format);

                return new HtmlString(rawHtml);
            }

            var slices = ExtractSlicesFromProbedString(matches, argumentReferences, probedString);

            return ConstructFinalHtmlContent(helper, arguments, slices);
        }

        private static IHtmlContent ConstructFinalHtmlContent(IHtmlHelper helper, object[] arguments, List<FormatSlice> slices)
        {
            var finalStringBuilder = new StringBuilder();

            foreach (var myItem in slices)
            {
                if (myItem.ArgumentReferenceInformation == null)
                {
                    var rawHtml = EncodeAndPreserveLineBreaks(
                        helper,
                        myItem.FormatString);

                    finalStringBuilder.Append(rawHtml);
                    continue;
                }

                var originalArgument = arguments[myItem.ArgumentReferenceInformation.ArgumentIndex];

                if (originalArgument is IHtmlContent htmlContent)
                {
                    using var stringWriter = new StringWriter();
                    htmlContent.WriteTo(stringWriter, HtmlEncoder.Default);
                    finalStringBuilder.Append(stringWriter);
                    continue;
                }

                var formattedRawHtml = EncodeAndPreserveLineBreaks(
                    helper,
                    myItem.ArgumentReferenceInformation.FormattedValue);
                finalStringBuilder.Append(formattedRawHtml);
            }

            return new HtmlString(finalStringBuilder.ToString());
        }

        private static string EncodeAndPreserveLineBreaks(
            IHtmlHelper helper,
            string input)
        {
            var rawHtml = helper.Encode(input);
            rawHtml = rawHtml.Replace("\n", "<br />");
            rawHtml = rawHtml.Replace("&#xA;", "<br />");
            return rawHtml;
        }

        private static string BuildRegexPattern(List<ArgumentReferenceInformation> argumentReferences)
        {
            var regexPattern = "(";
            var isFirst = true;

            foreach (var argumentReference in argumentReferences)
            {
                if (!isFirst)
                {
                    regexPattern += "|";
                }

                regexPattern += argumentReference.Guid;
                isFirst = false;
            }

            regexPattern += ")";
            return regexPattern;
        }

        private static string ProbeFormatString(
            string format,
            List<WrappedArgument> argumentsWrapped,
            out List<ArgumentReferenceInformation> argumentReferences)
        {
            var probingStringFormatter = new ProbingStringFormatter();
            var probedString = string.Format(probingStringFormatter, format, argumentsWrapped.Cast<object>().ToArray());

            argumentReferences = probingStringFormatter.ArgumentReferences;
            return probedString;
        }

        private static List<WrappedArgument> WrapArguments(object[] arguments)
        {
            var argumentsWrapped = new List<WrappedArgument>();

            for (var argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
            {
                var wrappedArgument = new WrappedArgument(arguments[argumentIndex], argumentIndex);
                argumentsWrapped.Add(wrappedArgument);
            }

            return argumentsWrapped;
        }

        private static List<FormatSlice> ExtractSlicesFromProbedString(
            MatchCollection matches,
            List<ArgumentReferenceInformation> argumentReferences,
            string probedString)
        {
            var lastMatchPosition = -1;
            var slices = new List<FormatSlice>();

            for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                var currentMatch = matches[matchIndex];

                var slice = new FormatSlice(
                    null,
                    argumentReferences.FirstOrDefault(r => r.Guid == currentMatch.Value));

                var matchPosition = currentMatch.Index;

                if (lastMatchPosition == -1)
                {
                    if (matchPosition == 0)
                    {
                        lastMatchPosition = matchPosition;
                        slices.Add(slice);
                        continue;
                    }

                    var prefixSlice = new FormatSlice(
                        probedString.Substring(0, matchPosition),
                        null);

                    slices.Add(prefixSlice);
                    slices.Add(slice);

                    lastMatchPosition = matchPosition;
                    continue;
                }

                var gapLength = matchPosition - (lastMatchPosition + 32);

                if (gapLength <= 0)
                {
                    slices.Add(slice);
                    lastMatchPosition = matchPosition;
                    continue;
                }

                var prefixSliceNew = new FormatSlice(
                    probedString.Substring(lastMatchPosition + 32, gapLength),
                    null);

                slices.Add(prefixSliceNew);
                slices.Add(slice);
                lastMatchPosition = matchPosition;
            }

            var charactersLeft = probedString.Length - (lastMatchPosition + 32);

            if (charactersLeft <= 0)
            {
                return slices;
            }

            var endingSlice = new FormatSlice(
                probedString.Substring(lastMatchPosition + 32),
                null);

            slices.Add(endingSlice);

            return slices;
        }

        private class FormatSlice
        {
            public FormatSlice(string formatString, ArgumentReferenceInformation argumentReferenceInformation)
            {
                this.FormatString = formatString;
                this.ArgumentReferenceInformation = argumentReferenceInformation;
            }

            public string FormatString { get; }

            public ArgumentReferenceInformation ArgumentReferenceInformation { get; }
        }

        private class WrappedArgument
        {
            public WrappedArgument(object value, int argumentIndex)
            {
                this.Value = value;
                this.ArgumentIndex = argumentIndex;
            }

            public object Value { get; }

            public int ArgumentIndex { get; }
        }

        private sealed class ProbingStringFormatter : IFormatProvider, ICustomFormatter
        {
            public ProbingStringFormatter()
            {
                this.ArgumentReferences = new List<ArgumentReferenceInformation>();
            }

            public List<ArgumentReferenceInformation> ArgumentReferences { get; }

            public object GetFormat(Type formatType)
            {
                return formatType == typeof(ICustomFormatter) ? this : null;
            }

            public string Format(
                string format,
                object arg,
                IFormatProvider formatProvider)
            {
                if (!this.Equals(formatProvider))
                {
                    return null;
                }

                if (!(arg is WrappedArgument wrappedArgument))
                {
                    throw new FormatException("This formatter is not meant for public use.");
                }

                var guid = Guid.NewGuid();

                // ReSharper disable FormatStringProblem
                var formattedValue = string.IsNullOrWhiteSpace(format)
                    ? $"{wrappedArgument.Value}"
                    : string.Format("{0:" + format + "}", wrappedArgument.Value);

                // ReSharper restore FormatStringProblem
                var guidAsString = guid.ToString("N");

                var argumentReferenceInformation = new ArgumentReferenceInformation(
                    wrappedArgument.ArgumentIndex,
                    guidAsString,
                    formattedValue);

                this.ArgumentReferences.Add(argumentReferenceInformation);

                return guidAsString;
            }
        }

        private class ArgumentReferenceInformation
        {
            public ArgumentReferenceInformation(
                int argumentIndex,
                string guid,
                string formattedValue)
            {
                this.ArgumentIndex = argumentIndex;
                this.Guid = guid;
                this.FormattedValue = formattedValue;
            }

            /// <summary>
            /// Gets the argument index of the reference.
            /// </summary>
            public int ArgumentIndex { get; }

            /// <summary>
            /// Gets the GUID representing the argument reference.
            /// </summary>
            public string Guid { get; }

            /// <summary>
            /// Gets the formatted value to use if the argument is not an HTML string.
            /// </summary>
            public string FormattedValue { get; }
        }
    }
}
