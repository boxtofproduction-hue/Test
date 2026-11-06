using System;
using System.Collections.Generic;
using System.Linq;

namespace GestionGym
{
    /// <summary>Tous les calculs : statuts des packs, crédits, revenus encaissés, contrôles des présences, statistiques.</summary>
    public class Calc
    {
        readonly Database db;
        public readonly Dictionary<string, Client> ClientById = new Dictionary<string, Client>();
        public readonly Dictionary<string, Pack> PackByName = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, Purchase> PurchaseById = new Dictionary<int, Purchase>();
        public readonly Dictionary<int, int> UsedByPurchase = new Dictionary<int, int>();
        public readonly Dictionary<string, int> PresencesByClient = new Dictionary<string, int>();
        readonly Checker allChecker;
        Dictionary<int, List<Issue>> control;

        public Calc(Database db)
        {
            this.db = db;
            foreach (Client c in db.Clients) ClientById[c.Id] = c;
            foreach (Pack p in db.Packs) PackByName[p.Name] = p;
            foreach (Purchase p in db.Purchases) PurchaseById[p.Id] = p;
            foreach (Attendance a in db.Attendances)
            {
                if (a.PurchaseId != 0)
                {
                    int n; UsedByPurchase.TryGetValue(a.PurchaseId, out n);
                    UsedByPurchase[a.PurchaseId] = n + 1;
                }
                int m; PresencesByClient.TryGetValue(a.ClientId, out m);
                PresencesByClient[a.ClientId] = m + 1;
            }
            allChecker = new Checker(db, this);
            foreach (Attendance a in db.Attendances) allChecker.Add(a);
        }

        public Checker Checker { get { return allChecker; } }

        public Pack PackOf(Purchase p) { Pack k; return PackByName.TryGetValue(p.PackName ?? "", out k) ? k : null; }

        public int UsedCredits(Purchase p)
        {
            int n; UsedByPurchase.TryGetValue(p.Id, out n);
            return n + p.CarriedUsed;
        }

        /// <summary>Crédits restants d'un pack à lot (null pour les crédits périodiques).</summary>
        public int? Remaining(Purchase p)
        {
            Pack k = PackOf(p);
            if (k == null || !k.IsPooled) return null;
            return k.Credits - UsedCredits(p);
        }

        /// <summary>Date limite d'utilisation des crédits d'un pack à lot.</summary>
        public DateTime? Expiry(Purchase p)
        {
            Pack k = PackOf(p);
            if (k == null || !k.IsPooled) return null;
            return p.Start.AddMonths(k.ValidityMonths);
        }

        /// <summary>Dernier jour de validité : date de fin saisie, sinon expiration (lot) ou fin d'engagement sans reconduction ; null = en cours.</summary>
        public DateTime? EffectiveEnd(Purchase p)
        {
            if (p.End.HasValue) return p.End;
            Pack k = PackOf(p);
            if (k == null) return null;
            if (k.IsPooled) return Expiry(p);
            if (k.EngagementMonths > 0 && !k.Renewal) return p.Start.AddMonths(k.EngagementMonths).AddDays(-1);
            return null;
        }

        public string Status(Purchase p)
        {
            DateTime today = db.AsOf;
            Pack k = PackOf(p);
            if (k == null) return "Pack inconnu";
            if (p.Start > today) return "À venir";
            if (p.End.HasValue && p.End.Value < today) return "Terminé";
            if (k.IsPooled)
            {
                int? rest = Remaining(p);
                if (rest.HasValue && rest.Value <= 0) return "Épuisé";
                DateTime? ex = Expiry(p);
                if (ex.HasValue && ex.Value < today) return "Expiré";
                return "Actif";
            }
            DateTime? end = EffectiveEnd(p);
            if (end.HasValue && end.Value < today) return "Terminé";
            return "Actif";
        }

        public bool IsActive(Purchase p) { return Status(p) == "Actif"; }

        // ------------------------------------------------------------ revenus (encaissés, base caisse)
        public int PaymentCount(Purchase p, DateTime asOf)
        {
            Pack k = PackOf(p);
            if (k == null || asOf < p.Start) return 0;
            DateTime x = asOf;
            if (p.End.HasValue && p.End.Value < x) x = p.End.Value;
            if (x < p.Start) return 0;
            int step = k.StepMonths;
            if (step == 0) return 1;
            int n = Fmt.MonthsBetween(p.Start, x) / step + 1;
            return Math.Min(k.PaymentCap, n);
        }

        public decimal Revenue(Purchase p, DateTime from, DateTime to)
        {
            Pack k = PackOf(p);
            if (k == null) return 0;
            return k.Price * (PaymentCount(p, to) - PaymentCount(p, from.AddDays(-1)));
        }

        public decimal YearRevenue(Purchase p)
        {
            return Revenue(p, new DateTime(db.Year, 1, 1), new DateTime(db.Year, 12, 31));
        }

        public decimal[] MonthlyRevenue(Purchase p)
        {
            var r = new decimal[12];
            for (int m = 1; m <= 12; m++)
                r[m - 1] = Revenue(p, new DateTime(db.Year, m, 1), new DateTime(db.Year, m, DateTime.DaysInMonth(db.Year, m)));
            return r;
        }

        // ------------------------------------------------------------ clients
        public string Kind(Purchase p) { Pack k = PackOf(p); return k == null ? "?" : k.Kind; }

        /// <summary>Statut du client et achat principal (abonnement actif, sinon pack actif, sinon dernier achat).</summary>
        public string ClientStatus(Client c, out Purchase main)
        {
            main = null;
            var mine = db.Purchases.Where(p => p.ClientId == c.Id).ToList();
            if (mine.Count == 0) return "Sans pack";
            var act = mine.Where(IsActive).ToList();
            if (act.Count > 0)
            {
                Purchase sub = act.Where(p => Kind(p) == "Abonnement").OrderByDescending(p => p.Start).FirstOrDefault();
                if (sub != null) { main = sub; return "Abonné"; }
                main = act.OrderByDescending(p => p.Start).First();
                return "Pack actif";
            }
            main = mine.OrderByDescending(p => p.Start).First();
            return "Inactif";
        }

        public string FavoriteActivity(string clientId)
        {
            var g = db.Attendances.Where(a => a.ClientId == clientId).GroupBy(a => a.Activity).OrderByDescending(x => x.Count()).FirstOrDefault();
            return g == null ? "" : g.Key;
        }

        public string FavoriteSlot(string clientId)
        {
            var g = db.Attendances.Where(a => a.ClientId == clientId).GroupBy(a => a.Slot).OrderByDescending(x => x.Count()).FirstOrDefault();
            return g == null ? "" : g.Key;
        }

        // ------------------------------------------------------------ présences : contrôles
        public Dictionary<int, List<Issue>> ControlAll()
        {
            if (control != null) return control;
            control = new Dictionary<int, List<Issue>>();
            var ch = new Checker(db, this);
            foreach (Attendance a in db.Attendances.OrderBy(x => x.Date).ThenBy(x => db.Slots.IndexOf(x.Slot)).ThenBy(x => x.Id))
            {
                control[a.Id] = ch.Check(a);
                ch.Add(a);
            }
            return control;
        }

        public static string IssueText(List<Issue> l)
        {
            if (l == null || l.Count == 0) return "OK";
            Issue w = l.OrderByDescending(i => (int)i.Severity).First();
            return (w.Severity == Sev.Error ? "⚠ " : "• ") + w.Text;
        }

        public static Sev Worst(List<Issue> l)
        {
            if (l == null || l.Count == 0) return Sev.Ok;
            return l.Max(i => i.Severity);
        }

        public List<Issue> PurchaseIssues(Purchase p)
        {
            var l = new List<Issue>();
            Pack k = PackOf(p);
            if (!ClientById.ContainsKey(p.ClientId)) l.Add(new Issue(Sev.Error, "Client inconnu"));
            if (k == null) l.Add(new Issue(Sev.Error, "Pack inconnu (supprimé du catalogue ?)"));
            if (p.End.HasValue && p.End.Value < p.Start) l.Add(new Issue(Sev.Error, "Fin avant début"));
            if (k != null && k.OncePerClient && db.Purchases.Count(x => x.ClientId == p.ClientId && x.PackName.Equals(p.PackName, StringComparison.OrdinalIgnoreCase)) > 1)
                l.Add(new Issue(Sev.Error, "Pack à usage unique acheté plusieurs fois"));
            if (k != null && k.IsPooled)
            {
                int? rest = Remaining(p);
                if (rest.HasValue && rest.Value < 0) l.Add(new Issue(Sev.Error, "Crédits dépassés"));
                DateTime? ex = Expiry(p);
                if (rest.HasValue && rest.Value > 0 && ex.HasValue && ex.Value >= db.AsOf && (ex.Value - db.AsOf).TotalDays <= db.AlertDays && !p.End.HasValue)
                    l.Add(new Issue(Sev.Warning, "Expire dans " + (int)(ex.Value - db.AsOf).TotalDays + " jour(s) avec " + rest.Value + " crédit(s) restant(s)"));
            }
            return l;
        }

        // ------------------------------------------------------------ statistiques
        public decimal[] RevenueByMonth()
        {
            var r = new decimal[12];
            foreach (Purchase p in db.Purchases)
            {
                decimal[] m = MonthlyRevenue(p);
                for (int i = 0; i < 12; i++) r[i] += m[i];
            }
            return r;
        }

        public int[] PresencesByMonth()
        {
            var r = new int[12];
            foreach (Attendance a in db.Attendances) if (a.Date.Year == db.Year) r[a.Date.Month - 1]++;
            return r;
        }

        public Dictionary<string, int> PresencesByActivity()
        {
            var d = new Dictionary<string, int>();
            foreach (string a in db.Activities) d[a] = 0;
            foreach (Attendance a in db.Attendances)
            {
                int n; d.TryGetValue(a.Activity, out n);
                d[a.Activity] = n + 1;
            }
            return d;
        }

        public Dictionary<string, int> PresencesBySlot()
        {
            var d = new Dictionary<string, int>();
            foreach (string s in db.Slots) d[s] = 0;
            foreach (Attendance a in db.Attendances)
            {
                int n; d.TryGetValue(a.Slot, out n);
                d[a.Slot] = n + 1;
            }
            return d;
        }

        public Dictionary<string, int> SalesByPack()
        {
            var d = new Dictionary<string, int>();
            foreach (Pack k in db.Packs) d[k.Name] = 0;
            foreach (Purchase p in db.Purchases)
            {
                if (p.Start.Year != db.Year) continue;
                int n; d.TryGetValue(p.PackName, out n);
                d[p.PackName] = n + 1;
            }
            return d;
        }

        /// <summary>Taux de remplissage moyen = présences / (séances programmées × capacité).</summary>
        public double FillRate()
        {
            int sessions = db.Sessions.Count(s => s.Date.Year == db.Year);
            if (sessions == 0) return 0;
            int pres = db.Attendances.Count(a => a.Date.Year == db.Year);
            return 100.0 * pres / ((double)sessions * db.Capacity);
        }
    }

    /// <summary>Vérifie une présence par rapport aux données existantes (comptes cumulés), en temps réel ou sur tout le journal.</summary>
    public class Checker
    {
        readonly Database db;
        readonly Calc calc;
        readonly Dictionary<string, int> slotCount = new Dictionary<string, int>();
        readonly HashSet<string> dups = new HashSet<string>();
        readonly Dictionary<int, int> used = new Dictionary<int, int>();
        readonly Dictionary<string, int> period = new Dictionary<string, int>();
        readonly Dictionary<string, string> sessions = new Dictionary<string, string>();

        public Checker(Database db, Calc calc)
        {
            this.db = db;
            this.calc = calc;
            foreach (Session s in db.Sessions) sessions[SlotKey(s.Date, s.Slot)] = s.Activity;
        }

        static string SlotKey(DateTime d, string slot) { return d.ToString("yyyyMMdd") + "|" + slot; }

        string PeriodKey(Pack k, Attendance a)
        {
            switch (k.Per)
            {
                case "Jour": return a.PurchaseId + "|" + a.Date.ToString("yyyyMMdd");
                case "Semaine": return a.PurchaseId + "|" + Fmt.MondayOf(a.Date).ToString("yyyyMMdd");
                case "Mois": return a.PurchaseId + "|" + a.Date.ToString("yyyyMM");
                case "An": return a.PurchaseId + "|" + a.Date.ToString("yyyy");
                default: return null;
            }
        }

        public void Add(Attendance a)
        {
            string sk = SlotKey(a.Date, a.Slot);
            int n; slotCount.TryGetValue(sk, out n);
            slotCount[sk] = n + 1;
            dups.Add(sk + "|" + a.ClientId);
            if (a.PurchaseId != 0)
            {
                int u; used.TryGetValue(a.PurchaseId, out u);
                used[a.PurchaseId] = u + 1;
                Purchase p;
                if (calc.PurchaseById.TryGetValue(a.PurchaseId, out p))
                {
                    Pack k = calc.PackOf(p);
                    if (k != null && !k.IsPooled)
                    {
                        string pk = PeriodKey(k, a);
                        int c; period.TryGetValue(pk, out c);
                        period[pk] = c + 1;
                    }
                }
            }
        }

        public int SlotCount(DateTime d, string slot)
        {
            int n; slotCount.TryGetValue(SlotKey(d, slot), out n);
            return n;
        }

        public List<Issue> Check(Attendance a)
        {
            var l = new List<Issue>();
            Client c;
            if (!calc.ClientById.TryGetValue(a.ClientId ?? "", out c)) l.Add(new Issue(Sev.Error, "Client inconnu"));
            if (a.Date.Year != db.Year) l.Add(new Issue(Sev.Error, "Date hors de l'année de la base (" + db.Year + ")"));
            if (!db.Slots.Contains(a.Slot ?? "")) l.Add(new Issue(Sev.Error, "Créneau inconnu"));
            if (string.IsNullOrEmpty(a.Activity) || !db.Activities.Contains(a.Activity)) l.Add(new Issue(Sev.Error, "Activité inconnue"));

            string sk = SlotKey(a.Date, a.Slot ?? "");
            if (dups.Contains(sk + "|" + a.ClientId)) l.Add(new Issue(Sev.Error, "Doublon : ce client est déjà présent à ce créneau"));

            string cfg;
            if (!sessions.TryGetValue(sk, out cfg)) l.Add(new Issue(Sev.Warning, "Aucune séance configurée à ce créneau"));
            else if (cfg != a.Activity) l.Add(new Issue(Sev.Warning, "Séance configurée : " + cfg));
            if (SlotCount(a.Date, a.Slot ?? "") >= db.Capacity) l.Add(new Issue(Sev.Warning, "Séance complète (" + db.Capacity + " places)"));

            if (a.PurchaseId == 0) l.Add(new Issue(Sev.Warning, "Aucun pack débité"));
            else l.AddRange(PackCheck(a));
            return l;
        }

        /// <summary>Contrôles propres au pack débité (période, expiration, Event/Normal, crédits, quota).</summary>
        public List<Issue> PackCheck(Attendance a)
        {
            var l = new List<Issue>();
            if (a.PurchaseId == 0) return l;
            Purchase p;
            if (!calc.PurchaseById.TryGetValue(a.PurchaseId, out p)) { l.Add(new Issue(Sev.Error, "Pack inconnu")); return l; }
            Pack k = calc.PackOf(p);
            if (k == null) { l.Add(new Issue(Sev.Error, "Pack supprimé du catalogue")); return l; }
            if (p.ClientId != a.ClientId) l.Add(new Issue(Sev.Error, "Pack d'un autre client"));
            if (a.Date < p.Start) l.Add(new Issue(Sev.Error, "Séance avant le début du pack (" + Fmt.Date(p.Start) + ")"));
            if (p.End.HasValue && a.Date > p.End.Value) l.Add(new Issue(Sev.Error, "Pack terminé le " + Fmt.Date(p.End)));
            DateTime? ex = calc.Expiry(p);
            if (ex.HasValue && a.Date > ex.Value) l.Add(new Issue(Sev.Error, "Pack expiré le " + Fmt.Date(ex)));
            if (!k.IsPooled)
            {
                DateTime? ee = calc.EffectiveEnd(p);
                if (ee.HasValue && a.Date > ee.Value) l.Add(new Issue(Sev.Error, "Engagement terminé le " + Fmt.Date(ee)));
            }
            bool isEventAct = a.Activity == db.EventActivity;
            if (k.IsEvent && !isEventAct) l.Add(new Issue(Sev.Error, "Pack Event : uniquement les séances « " + db.EventActivity + " »"));
            if (!k.IsEvent && isEventAct && !db.NormalInEvent) l.Add(new Issue(Sev.Error, "Pack Normal : les séances « " + db.EventActivity + " » ne sont pas incluses"));
            if (k.IsPooled)
            {
                int u; used.TryGetValue(a.PurchaseId, out u);
                if (u + p.CarriedUsed >= k.Credits) l.Add(new Issue(Sev.Error, "Crédits épuisés (" + k.Credits + " séance(s))"));
            }
            else
            {
                int cnt; period.TryGetValue(PeriodKey(k, a), out cnt);
                if (cnt >= k.Credits) l.Add(new Issue(Sev.Warning, "Quota atteint : " + k.Credits + " séance(s)" + Lists.PerText(k.Per)));
            }
            return l;
        }
    }
}
