﻿using Nikse.SubtitleEdit.Core.Cea708;
using Nikse.SubtitleEdit.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.Core.SubtitleFormats
{
    public class MacCaption10 : SubtitleFormat
    {
        private static readonly Regex RegexTimeCodes = new Regex(@"^\d\d:\d\d:\d\d:\d\d\t", RegexOptions.Compiled);

        public override string Extension => ".mcc";

        public override string Name => "MacCaption 1.0";

        public override string ToText(Subtitle subtitle, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"File Format=MacCaption_MCC V1.0
///////////////////////////////////////////////////////////////////////////////////
// Computer Prompting and Captioning Company
// Ancillary Data Packet Transfer File
//
// Permission to generate this format is granted provided that
// 1. This ANC Transfer file format is used on an as-is basis and no warranty is given, and
// 2. This entire descriptive information text is included in a generated .mcc file.
//
// General file format:
// HH:MM:SS:FF(tab)[Hexadecimal ANC data in groups of 2 characters]
// Hexadecimal data starts with the Ancillary Data Packet DID (Data ID defined in S291M)
// and concludes with the Check Sum following the User Data Words.
// Each time code line must contain at most one complete ancillary data packet.
// To transfer additional ANC Data successive lines may contain identical time code.
// Time Code Rate=[24, 25, 30, 30DF, 50, 60]
//
// ANC data bytes may be represented by one ASCII character according to the following schema:
// G FAh 00h 00h
// H 2 x (FAh 00h 00h)
// I 3 x (FAh 00h 00h)
// J 4 x (FAh 00h 00h)
// K 5 x (FAh 00h 00h)
// L 6 x (FAh 00h 00h)
// M 7 x (FAh 00h 00h)
// N 8 x (FAh 00h 00h)
// O 9 x (FAh 00h 00h)
// P FBh 80h 80h
// Q FCh 80h 80h
// R FDh 80h 80h
// S 96h 69h
// T 61h 01h
// U E1h 00h 00h
// Z 00h
//
///////////////////////////////////////////////////////////////////////////////////");
            sb.AppendLine();
            sb.AppendLine("UUID=" + Guid.NewGuid().ToString().ToUpperInvariant());// UUID=9F6112F4-D9D0-4AAF-AA95-854710D3B57A
            sb.AppendLine("Creation Program=Subtitle Edit");
            sb.AppendLine("Creation Date=" + DateTime.Now.ToLongDateString());
            sb.AppendLine("Creation Time=" + DateTime.Now.ToShortTimeString());
            sb.AppendLine();

            for (int i = 0; i < subtitle.Paragraphs.Count; i++)
            {
                var p = subtitle.Paragraphs[i];
                sb.AppendLine($"{ToTimeCode(p.StartTime.TotalMilliseconds)}\t{p.Text}"); // TODO: Encode text - how???
                sb.AppendLine();

                var next = subtitle.GetParagraphOrDefault(i + 1);
                if (next == null || Math.Abs(next.StartTime.TotalMilliseconds - p.EndTime.TotalMilliseconds) > 100)
                {
                    sb.AppendLine($"{ToTimeCode(p.EndTime.TotalMilliseconds)}\t???"); // TODO: Some end text???
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        private static string ToTimeCode(double totalMilliseconds)
        {
            var ts = TimeSpan.FromMilliseconds(totalMilliseconds);
            return $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}:{MillisecondsToFramesMaxFrameRate(ts.Milliseconds):00}";
        }

        public override void LoadSubtitle(Subtitle subtitle, List<string> lines, string fileName)
        {
            _errorCount = 0;
            subtitle.Paragraphs.Clear();
            Paragraph p = null;
            var header = new StringBuilder();
            char[] splitChars = { ':', ';', ',' };
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var s = line.Trim();
                if (string.IsNullOrEmpty(s) || s.StartsWith("//", StringComparison.Ordinal) || s.StartsWith("File Format=MacCaption_MCC", StringComparison.Ordinal) || s.StartsWith("UUID=", StringComparison.Ordinal) ||
                    s.StartsWith("Creation Program=") || s.StartsWith("Creation Date=") || s.StartsWith("Creation Time=") ||
                    s.StartsWith("Code Rate=", StringComparison.Ordinal) || s.StartsWith("Time Code Rate=", StringComparison.Ordinal))
                {
                    header.AppendLine(line);
                }
                else
                {
                    var match = RegexTimeCodes.Match(s);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var startTime = DecodeTimeCodeFrames(s.Substring(0, match.Length - 1), splitChars);
                    var text = GetText(index, s.Substring(match.Index + match.Length).Trim(), index == lines.Count - 1);
                    if (string.IsNullOrEmpty(text))
                    {
                        if (p != null)
                        {
                            p.EndTime = new TimeCode(startTime.TotalMilliseconds);
                        }

                        continue;
                    }

                    p = new Paragraph(startTime, new TimeCode(startTime.TotalMilliseconds), text);
                    subtitle.Paragraphs.Add(p);
                }
            }

            for (var i = subtitle.Paragraphs.Count - 2; i >= 0; i--)
            {
                p = subtitle.GetParagraphOrDefault(i);
                var next = subtitle.GetParagraphOrDefault(i + 1);
                if (p != null && next != null && Math.Abs(p.EndTime.TotalMilliseconds - p.StartTime.TotalMilliseconds) < 0.001)
                {
                    p.EndTime = new TimeCode(next.StartTime.TotalMilliseconds);
                }

                if (next != null && string.IsNullOrEmpty(next.Text))
                {
                    subtitle.Paragraphs.Remove(next);
                }
            }
            p = subtitle.GetParagraphOrDefault(0);
            if (p != null && string.IsNullOrEmpty(p.Text))
            {
                subtitle.Paragraphs.Remove(p);
            }

            subtitle.Renumber();
        }

        public static string GetText(int lineIndex, string input, bool flush)
        {
            var hexString = GetHex(input);
            var bytes = HexStringToByteArray(hexString);
            if (bytes.Length < 10)
            {
                return string.Empty;
            }

            var cea708 = new Smpte291M(bytes);
            return cea708.GetText(lineIndex, flush);
        }

        private static string GetHex(string input)
        {
            // ANC data bytes may be represented by one ASCII character according to the following schema:
            var dictionary = new Dictionary<char, string>
            {
                { 'G', "FA0000" },
                { 'H', "FA0000FA0000" },
                { 'I', "FA0000FA0000FA0000" },
                { 'J', "FA0000FA0000FA0000FA0000" },
                { 'K', "FA0000FA0000FA0000FA0000FA0000" },
                { 'L', "FA0000FA0000FA0000FA0000FA0000FA0000" },
                { 'M', "FA0000FA0000FA0000FA0000FA0000FA0000FA0000" },
                { 'N', "FA0000FA0000FA0000FA0000FA0000FA0000FA0000FA0000" },
                { 'O', "FA0000FA0000FA0000FA0000FA0000FA0000FA0000FA0000FA0000" },
                { 'P', "FB8080" },
                { 'Q', "FC8080" },
                { 'R', "FD8080" },
                { 'S', "9669" },
                { 'T', "6101" },
                { 'U', "E1000000" },
                { 'Z', "00" },
            };

            var sb = new StringBuilder();
            foreach (var ch in input)
            {
                if (dictionary.TryGetValue(ch, out var hexValue))
                {
                    sb.Append(hexValue);
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }


        private static byte[] HexStringToByteArray(string hex)
        {
            var numberChars = hex.Length;
            var bytes = new byte[numberChars / 2];
            for (var i = 0; i < numberChars - 1; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return bytes;
        }
    }
}