using Audio.Core.Consts;
using Audio.Core.Enums;
using Audio.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Core.Extensions
{
    public static class StringExtension
    {
        public static bool ConvertToTcpData(this string input,out TcpDataTemplate result)
        {
            result = new TcpDataTemplate();
            if (string.IsNullOrEmpty(input)|| input.Length < 19)
            {
                return false;
            }
            try
            {
                // 4位报文头
                var headerStr = input.Substring(0, 4);
                if (!Enum.TryParse(headerStr, out CommandHeaderEnum header))
                {
                    Console.WriteLine($"无效的报文头: {headerStr}");
                    return false;
                }

                // 14位时间（假设格式为yyyyMMddHHmmss）
                var timeStr = input.Substring(4, 14);
                var timestamp = DateTime.TryParseExact(timeStr, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt : DateTime.MinValue;

                // 1位内容头
                char contentHead = input[18];

                // 内容
                string content = input.Substring(19);

                result = new TcpDataTemplate
                {
                    Header = header,
                    Timestamp = timestamp,
                    ContentHeader = contentHead,
                    Body = content,
                };

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"转换失败: {ex.Message}");
                return false;
            }
        }

        public static string AddFooter(this string input)
        {
            return $"{input}{VoskConst.Footer}";
        }

        public static bool TryToSearch(this string input, out string res)
        {
            res = string.Empty;
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            // 定义查询相关的关键词
            var queryKeywords = new[] { "查询", "查找", "查一下", "定位" };

            // 判断是否包含查询关键词
            if (queryKeywords.Any(keyword => input.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {// 转换中文数字为阿拉伯数字
                string convertedInput = input.ConvertChineseNumbersToArabic();

                // 使用正则表达式提取所有阿拉伯数字
                var digits = System.Text.RegularExpressions.Regex.Matches(convertedInput, @"\d+")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(match => match.Value);

                // 将提取的数字拼接成一个字符串并赋值给 input
                res = string.Join(string.Empty, digits);

                return true;
            }
            else
            {
                // 如果不包含查询关键词，则返回false
                return false;
            }
        }

        public static string ConvertChineseNumbersToArabic(this string input)
        {
            // 示例：将中文数字转换为阿拉伯数字
            var chineseToArabic = new Dictionary<string, string>
            {
                { "零", "0" }, { "一", "1" }, { "二", "2" }, { "三", "3" },
                { "四", "4" }, { "五", "5" }, { "六", "6" }, { "七", "7" },
                { "八", "8" }, { "九", "9" }, { "十", "10" }
            };

            foreach (var kvp in chineseToArabic)
            {
                input = input.Replace(kvp.Key, kvp.Value);
            }

            return input;
        }
    }
}
