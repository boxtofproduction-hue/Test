using System;
using System.Globalization;
using System.Text;

namespace GestionGym
{
    /// <summary>Formats français, lecture tolérante des dates et des nombres saisis à la main.</summary>
    public static class Fmt
    {
        public static readonly CultureInfo Fr = new CultureInfo("fr-FR");
        static readonly string[] DateFormats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yy", "d/M/yy", "yyyy/MM/dd" };

        public static string Date(DateTime d) { return d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); }
        public static string Date(DateTime? d) { return d.HasValue ? Date(d.Value) : ""; }

        public static bool TryDate(string s, out DateTime d)
        {
            d = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            // Date issue d'Excel (numéro de série) : 30000 à 80000 ~ années 1982 à 2119
            double serial;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out serial) && serial >= 30000 && serial <= 80000)
            {
                d = DateTime.FromOADate(Math.Floor(serial));
                return true;
            }
            if (s.Length > 10 && (s[10] == ' ' || s[10] == 'T')) s = s.Substring(0, 10);
            DateTime r;
            if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out r))
            {
                if (r.Year < 1990 || r.Year > 2200) return false;
                d = r.Date;
                return true;
            }
            return false;
        }

        public static string Dec(decimal v) { return v.ToString("0.##", Fr); }
        public static string Money(decimal v) { return v.ToString("N2", Fr) + " €"; }
        public static string Money0(decimal v) { return v.ToString("N0", Fr) + " €"; }
        public static string Pct(double v) { return v.ToString("0.0", Fr) + " %"; }

        /// <summary>Lit un nombre décimal saisi avec virgule ou point, espaces et symbole € tolérés.</summary>
        public static bool TryDec(string s, out decimal v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Replace("€", "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Trim();
            if (s.Length == 0) return false;
            int lc = s.LastIndexOf(','), lp = s.LastIndexOf('.');
            if (lc >= 0 && lp >= 0)
            {
                if (lc > lp) s = s.Replace(".", "").Replace(',', '.');
                else s = s.Replace(",", "");
            }
            else if (lc >= 0) s = s.Replace(',', '.');
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v);
        }

        public static bool TryInt(string s, out int v)
        {
            v = 0;
            decimal d;
            if (!TryDec(s, out d)) return false;
            if (d != Math.Floor(d) || d > int.MaxValue || d < int.MinValue) return false;
            v = (int)d;
            return true;
        }

        /// <summary>Nettoie un texte saisi : espaces, caractères de contrôle, préfixes de formule (= + @).</summary>
        public static string Clean(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\t' || c == '\r' || c == '\n') sb.Append(' ');
                else if (!char.IsControl(c)) sb.Append(c);
            }
            string t = sb.ToString().Trim();
            while (t.Length > 0 && (t[0] == '=' || t[0] == '+' || t[0] == '@')) t = t.Substring(1).TrimStart();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t;
        }

        /// <summary>Texte sans accents ni majuscules, pour les recherches.</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().ToLowerInvariant();
        }

        public static string MonthName(int m)
        {
            string[] n = { "Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre" };
            return (m >= 1 && m <= 12) ? n[m - 1] : "";
        }

        public static string Plural(int n, string one, string many) { return n + " " + (n > 1 ? many : one); }

        /// <summary>Différence en mois entiers entre deux dates (comme DATEDIF "m" d'Excel).</summary>
        public static int MonthsBetween(DateTime from, DateTime to)
        {
            int m = (to.Year - from.Year) * 12 + to.Month - from.Month;
            if (to.Day < from.Day) m--;
            return m < 0 ? 0 : m;
        }

        public static DateTime MondayOf(DateTime d)
        {
            int off = ((int)d.DayOfWeek + 6) % 7; // lundi = 0
            return d.Date.AddDays(-off);
        }
    }
}
