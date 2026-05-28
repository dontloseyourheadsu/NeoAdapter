using System;
using System.Collections.Generic;
using System.Text;

namespace NeoAdapter.Application.SqlEditor;

public static class SqlValidation
{
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", 
        "CREATE", "MERGE", "REPLACE", "RENAME", "GRANT", "REVOKE"
    };

    public static string CleanSql(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return string.Empty;

        var sb = new StringBuilder();
        int i = 0;
        int len = sql.Length;

        while (i < len)
        {
            // 1. Check for single line comment
            if (i < len - 1 && sql[i] == '-' && sql[i + 1] == '-')
            {
                i += 2;
                while (i < len && sql[i] != '\n' && sql[i] != '\r')
                {
                    i++;
                }
                sb.Append(' '); // replace comment with a space
                continue;
            }

            // 2. Check for block comment (potentially nested)
            if (i < len - 1 && sql[i] == '/' && sql[i + 1] == '*')
            {
                int depth = 1;
                i += 2;
                while (i < len && depth > 0)
                {
                    if (i < len - 1 && sql[i] == '/' && sql[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (i < len - 1 && sql[i] == '*' && sql[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }
                sb.Append(' ');
                continue;
            }

            // 3. Check for single quote string
            if (sql[i] == '\'')
            {
                i++; // skip open quote
                while (i < len)
                {
                    if (sql[i] == '\'')
                    {
                        // Check if it's an escaped single quote ''
                        if (i < len - 1 && sql[i + 1] == '\'')
                        {
                            i += 2; // skip both
                            continue;
                        }
                        else
                        {
                            i++; // skip close quote
                            break;
                        }
                    }
                    i++;
                }
                sb.Append(" '' "); // replace string content with empty string literal
                continue;
            }

            // 4. Check for double quote string
            if (sql[i] == '"')
            {
                i++;
                while (i < len)
                {
                    if (sql[i] == '"')
                    {
                        if (i < len - 1 && sql[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        else
                        {
                            i++;
                            break;
                        }
                    }
                    i++;
                }
                sb.Append(" \"\" ");
                continue;
            }

            // Normal character
            sb.Append(sql[i]);
            i++;
        }

        return sb.ToString();
    }

    public static bool IsQueryAllowed(string sql, out string forbiddenKeyword)
    {
        forbiddenKeyword = string.Empty;
        var cleaned = CleanSql(sql);

        var sb = new StringBuilder();
        for (int i = 0; i < cleaned.Length; i++)
        {
            char c = cleaned[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                if (sb.Length > 0)
                {
                    var token = sb.ToString();
                    if (ForbiddenKeywords.Contains(token))
                    {
                        forbiddenKeyword = token.ToUpper();
                        return false;
                    }
                    sb.Clear();
                }
            }
        }

        if (sb.Length > 0)
        {
            var token = sb.ToString();
            if (ForbiddenKeywords.Contains(token))
            {
                forbiddenKeyword = token.ToUpper();
                return false;
            }
        }

        return true;
    }
}
