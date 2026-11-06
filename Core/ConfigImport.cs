using System;
using System.Collections.Generic;
using System.Linq;

namespace GestionGym
{
    public class ConfigSection
    {
        public string Key;
        public string Label;
        public string Help;
    }

    /// <summary>Importe tout ou partie de la configuration d'une autre base : activités, créneaux, packs/tarifs, paramètres, programme.</summary>
    public static class ConfigImport
    {
        public static readonly ConfigSection[] Sections =
        {
            new ConfigSection { Key = "activites", Label = "Activités", Help = "Liste des activités (musculation, yoga…)" },
            new ConfigSection { Key = "creneaux", Label = "Créneaux horaires", Help = "Liste des créneaux de la journée" },
            new ConfigSection { Key = "packs", Label = "Packs et tarifs", Help = "Catalogue des packs : crédits, validité, engagement, paiement, éligibilité, prix" },
            new ConfigSection { Key = "parametres", Label = "Paramètres généraux", Help = "Capacité par séance, activité Event, alerte d'expiration…" },
            new ConfigSection { Key = "fiscal", Label = "Profil et taux URSSAF / CFE / rentabilité", Help = "Profil URSSAF, taux 2026, profil CFE, barèmes, paramètres de fidélité" },
            new ConfigSection { Key = "charges", Label = "Charges fixes", Help = "Loyer, énergie, assurances… (dates décalées sur la nouvelle année)" },
            new ConfigSection { Key = "programme", Label = "Programme des séances", Help = "Séances par jour et créneau + nature des jours (Vacances), recalées sur la nouvelle année" }
        };

        /// <summary>Décale une date d'une année à une autre en conservant le jour de la semaine (si demandé).</summary>
        public static DateTime? Shift(DateTime d, int toYear, bool alignWeekday)
        {
            int day = d.Day;
            if (d.Month == 2 && day == 29 && !DateTime.IsLeapYear(toYear)) day = 28;
            DateTime c = new DateTime(toYear, d.Month, day);
            if (alignWeekday)
            {
                int off = ((int)d.DayOfWeek - (int)c.DayOfWeek + 7) % 7;
                if (off > 3) off -= 7;
                c = c.AddDays(off);
            }
            return c.Year == toYear ? (DateTime?)c : null;
        }

        /// <summary>Applique les sections choisies de src vers dst. Retourne un compte rendu.</summary>
        public static string Run(Database src, Database dst, IEnumerable<string> keys, bool replace, bool alignWeekday)
        {
            var set = new HashSet<string>(keys);
            var rep = new List<string>();
            if (set.Contains("activites")) rep.Add(Activities(src, dst, replace));
            if (set.Contains("creneaux")) rep.Add(Slots(src, dst, replace));
            if (set.Contains("packs")) rep.Add(Packs(src, dst, replace));
            if (set.Contains("parametres")) rep.Add(Params(src, dst, replace));
            if (set.Contains("fiscal")) rep.Add(Fiscal(src, dst, replace));
            if (set.Contains("charges")) rep.Add(ChargesImport(src, dst, replace));
            if (set.Contains("programme")) rep.Add(Programme(src, dst, replace, alignWeekday));
            dst.Touch();
            return string.Join("\n", rep.Where(x => x.Length > 0));
        }

        static string Activities(Database src, Database dst, bool replace)
        {
            int added = 0, kept = 0, skipped = 0;
            if (replace)
            {
                var keep = dst.Activities.Where(a => !src.Activities.Contains(a, StringComparer.OrdinalIgnoreCase) &&
                    (dst.Sessions.Any(s => s.Activity == a) || dst.Attendances.Any(t => t.Activity == a) || dst.EventActivity == a)).ToList();
                dst.Activities.Clear();
                foreach (string a in src.Activities) { if (dst.Activities.Count < 12) { dst.Activities.Add(a); added++; } else skipped++; }
                foreach (string a in keep) { if (dst.Activities.Count < 12) { dst.Activities.Add(a); kept++; } else skipped++; }
            }
            else
            {
                foreach (string a in src.Activities)
                {
                    if (dst.Activities.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase))) continue;
                    if (dst.Activities.Count >= 12) { skipped++; continue; }
                    dst.Activities.Add(a); added++;
                }
            }
            return "Activités : " + added + " importée(s)" + (kept > 0 ? ", " + kept + " conservée(s) car utilisée(s)" : "") + (skipped > 0 ? ", " + skipped + " ignorée(s) (12 maximum)" : "") + ".";
        }

        static string Slots(Database src, Database dst, bool replace)
        {
            int added = 0, kept = 0, skipped = 0;
            if (replace)
            {
                var keep = dst.Slots.Where(a => !src.Slots.Contains(a, StringComparer.OrdinalIgnoreCase) &&
                    (dst.Sessions.Any(s => s.Slot == a) || dst.Attendances.Any(t => t.Slot == a))).ToList();
                dst.Slots.Clear();
                foreach (string a in src.Slots) { if (dst.Slots.Count < 20) { dst.Slots.Add(a); added++; } else skipped++; }
                foreach (string a in keep) { if (dst.Slots.Count < 20) { dst.Slots.Add(a); kept++; } else skipped++; }
            }
            else
            {
                foreach (string a in src.Slots)
                {
                    if (dst.Slots.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase))) continue;
                    if (dst.Slots.Count >= 20) { skipped++; continue; }
                    dst.Slots.Add(a); added++;
                }
            }
            return "Créneaux : " + added + " importé(s)" + (kept > 0 ? ", " + kept + " conservé(s) car utilisé(s)" : "") + (skipped > 0 ? ", " + skipped + " ignoré(s) (20 maximum)" : "") + ".";
        }

        static string Packs(Database src, Database dst, bool replace)
        {
            int added = 0, updated = 0, removed = 0, kept = 0;
            foreach (Pack p in src.Packs)
            {
                int i = dst.Packs.FindIndex(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                if (i < 0) { dst.Packs.Add(p.Clone()); added++; }
                else if (replace) { Pack c = p.Clone(); c.Name = dst.Packs[i].Name; dst.Packs[i] = c; updated++; }
            }
            if (replace)
            {
                foreach (Pack p in dst.Packs.ToList())
                {
                    if (src.Packs.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    if (dst.SalesCount(p.Name) > 0) { p.Active = false; kept++; }
                    else { dst.Packs.Remove(p); removed++; }
                }
            }
            return "Packs : " + added + " ajouté(s), " + updated + " mis à jour, " + removed + " supprimé(s)" + (kept > 0 ? ", " + kept + " désactivé(s) car déjà vendus" : "") + ".";
        }

        static string Params(Database src, Database dst, bool replace)
        {
            int n = 0;
            foreach (var kv in src.Settings)
            {
                if (Fld.IsFiscalKey(kv.Key)) continue;
                if (!replace && dst.Settings.ContainsKey(kv.Key)) continue;
                dst.Settings[kv.Key] = kv.Value;
                n++;
            }
            return "Paramètres : " + n + " valeur(s) importée(s).";
        }

        static string Fiscal(Database src, Database dst, bool replace)
        {
            int n = 0;
            foreach (var kv in src.Settings)
            {
                if (!Fld.IsFiscalKey(kv.Key)) continue;
                if (!replace && dst.Settings.ContainsKey(kv.Key)) continue;
                dst.Settings[kv.Key] = kv.Value;
                n++;
            }
            return "Profil et taux URSSAF / CFE / rentabilité : " + n + " valeur(s) importée(s).";
        }

        static string ChargesImport(Database src, Database dst, bool replace)
        {
            int added = 0, dup = 0;
            if (replace) dst.Charges.Clear();
            foreach (Charge c in src.Charges)
            {
                Charge k = c.Clone();
                // une charge qui avait commencé dans l'année source garde son jour/mois ; les récurrentes sont conservées telles quelles
                if (k.Frequency == "Ponctuel")
                {
                    DateTime? d = Shift(k.Start, dst.Year, false);
                    if (!d.HasValue) continue;
                    k.Start = d.Value;
                    if (k.End.HasValue) { DateTime? e = Shift(k.End.Value, dst.Year, false); k.End = e; }
                }
                if (dst.Charges.Any(x => x.Label.Equals(k.Label, StringComparison.OrdinalIgnoreCase) && x.Frequency == k.Frequency && x.Amount == k.Amount)) { dup++; continue; }
                dst.Charges.Add(k); added++;
            }
            return "Charges fixes : " + added + " importée(s)" + (dup > 0 ? ", " + dup + " déjà présente(s)" : "") + ".";
        }

        static string Programme(Database src, Database dst, bool replace, bool align)
        {
            int added = 0, skippedAct = 0, skippedDate = 0, days = 0;
            if (replace)
            {
                dst.Sessions.RemoveAll(s => !dst.Attendances.Any(a => a.Date == s.Date && a.Slot == s.Slot));
                dst.Days.Clear();
            }
            foreach (Session s in src.Sessions)
            {
                DateTime? nd = Shift(s.Date, dst.Year, align);
                if (!nd.HasValue) { skippedDate++; continue; }
                if (!dst.Activities.Contains(s.Activity) || !dst.Slots.Contains(s.Slot)) { skippedAct++; continue; }
                if (dst.Sessions.Any(x => x.Date == nd.Value && x.Slot == s.Slot)) continue;
                dst.Sessions.Add(new Session { Date = nd.Value, Slot = s.Slot, Activity = s.Activity });
                added++;
            }
            foreach (DayInfo d in src.Days)
            {
                DateTime? nd = Shift(d.Date, dst.Year, align);
                if (!nd.HasValue) continue;
                if (dst.Days.Any(x => x.Date == nd.Value)) continue;
                dst.Days.Add(new DayInfo { Date = nd.Value, Nature = d.Nature });
                days++;
            }
            return "Programme : " + added + " séance(s) importée(s), " + days + " jour(s) « Vacances »" + (skippedAct > 0 ? ", " + skippedAct + " ignorée(s) (activité ou créneau absent de la base)" : "") + (skippedDate > 0 ? ", " + skippedDate + " hors année" : "") + ".";
        }
    }
}
