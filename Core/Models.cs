using System;
using System.Collections.Generic;

namespace GestionGym
{
    public static class Lists
    {
        public static readonly string[] Categories = { "Normal", "Event" };
        public static readonly string[] Pers = { "Total", "Jour", "Semaine", "Mois", "An" };
        public static readonly string[] Engagements = { "Aucun", "Mensuel", "Annuel" };
        public static readonly string[] Payments = { "Unique", "Mensuel", "Annuel" };
        public static readonly string[] Eligibilities = { "N.A", "Une fois par client" };
        public static readonly string[] Sexes = { "Homme", "Femme", "Autre" };
        public static readonly string[] Natures = { "Normal", "Vacances" };
        public const string OncePerClient = "Une fois par client";

        public static string PerText(string per)
        {
            switch (per)
            {
                case "Jour": return " par jour";
                case "Semaine": return " par semaine";
                case "Mois": return " par mois";
                case "An": return " par an";
                default: return "";
            }
        }
    }

    public class Pack
    {
        public string Name = "";
        public string Category = "Normal";
        public int Credits = 1;
        public string Per = "Total";
        public int ValidityMonths = 1;
        public string Engagement = "Aucun";
        public int EngagementMonths = 0;
        public string Payment = "Unique";
        public string Eligibility = "N.A";
        public bool Renewal = false;
        public decimal Price = 0;
        public bool Active = true;

        public bool IsEvent { get { return Category == "Event"; } }
        public bool IsPooled { get { return Per == "Total"; } }
        public bool OncePerClient { get { return Eligibility == Lists.OncePerClient; } }
        public int StepMonths { get { return Payment == "Mensuel" ? 1 : (Payment == "Annuel" ? 12 : 0); } }

        /// <summary>Nombre d'échéances dues sur l'engagement (9999 = illimité, reconduction).</summary>
        public int PaymentCap
        {
            get
            {
                int step = StepMonths;
                if (step == 0) return 1;
                if (Renewal || EngagementMonths <= 0) return 9999;
                return (int)Math.Ceiling((double)EngagementMonths / step);
            }
        }

        public decimal TotalValue
        {
            get
            {
                int cap = PaymentCap;
                return cap >= 9999 ? Price : Price * cap;
            }
        }

        /// <summary>Séances incluses estimées sur l'engagement (pack à lot : le lot).</summary>
        public int? IncludedSessions
        {
            get
            {
                if (IsPooled) return Credits;
                if (EngagementMonths <= 0) return null;
                double f;
                switch (Per)
                {
                    case "Jour": f = 365.0 / 12; break;
                    case "Semaine": f = 52.0 / 12; break;
                    case "Mois": f = 1; break;
                    default: f = 1.0 / 12; break;
                }
                return (int)Math.Round(Credits * EngagementMonths * f);
            }
        }

        public decimal? PricePerSession
        {
            get
            {
                int? n = IncludedSessions;
                if (!n.HasValue || n.Value <= 0) return null;
                return TotalValue / n.Value;
            }
        }

        /// <summary>Pack "Abonnement" : engagement ou crédits périodiques ; sinon "Pack séances" ; "Event" si catégorie Event.</summary>
        public string Kind
        {
            get
            {
                if (IsEvent) return "Event";
                if (Engagement != "Aucun" || !IsPooled) return "Abonnement";
                return "Pack séances";
            }
        }

        public string Summary
        {
            get
            {
                return Category + " – " + Credits + " séance(s)" + Lists.PerText(Per) + " – validité " + ValidityMonths + " mois – engagement " + Engagement.ToLowerInvariant()
                    + (EngagementMonths > 0 ? " (" + EngagementMonths + " mois)" : "") + " – paiement " + Payment.ToLowerInvariant() + " – " + Fmt.Money(Price)
                    + (OncePerClient ? " – une fois par client" : "");
            }
        }

        public Pack Clone() { return (Pack)MemberwiseClone(); }

        public static readonly string[] Header = { "Nom", "Categorie", "Credits", "CreditsPar", "ValiditeMois", "Engagement", "DureeEngagementMois", "Echeance", "Eligibilite", "ReconductionTacite", "Prix", "Actif" };

        public string[] ToCsv()
        {
            return new[] { Name, Category, Credits.ToString(), Per, ValidityMonths.ToString(), Engagement, EngagementMonths.ToString(), Payment, Eligibility, Renewal ? "Oui" : "Non", Fmt.Dec(Price), Active ? "Oui" : "Non" };
        }

        public static bool YesNo(string s) { s = Fmt.Norm(s); return s == "oui" || s == "true" || s == "1" || s == "vrai" || s == "o" || s == "yes"; }

        /// <summary>Retourne null si la ligne est valide, sinon le motif du rejet.</summary>
        public static string FromCsv(CsvTable t, string[] r, out Pack p)
        {
            p = new Pack();
            p.Name = Fmt.Clean(t.Get(r, "Nom"));
            if (p.Name.Length == 0) return "nom de pack vide";
            string v = t.Get(r, "Categorie");
            p.Category = Fmt.Norm(v) == "event" ? "Event" : "Normal";
            int i;
            if (!Fmt.TryInt(t.Get(r, "Credits"), out i) || i < 1) return "crédits invalides pour « " + p.Name + " »";
            p.Credits = i;
            string per = t.Get(r, "CreditsPar");
            p.Per = Match(Lists.Pers, per, "Total");
            if (!Fmt.TryInt(t.Get(r, "ValiditeMois"), out i) || i < 1) i = 1;
            p.ValidityMonths = Math.Min(i, 12);
            p.Engagement = Match(Lists.Engagements, t.Get(r, "Engagement"), "Aucun");
            if (!Fmt.TryInt(t.Get(r, "DureeEngagementMois"), out i) || i < 0) i = 0;
            p.EngagementMonths = i;
            p.Payment = Match(Lists.Payments, t.Get(r, "Echeance"), "Unique");
            p.Eligibility = Fmt.Norm(t.Get(r, "Eligibilite")).StartsWith("une fois") ? Lists.OncePerClient : "N.A";
            p.Renewal = YesNo(t.Get(r, "ReconductionTacite"));
            decimal d;
            if (!Fmt.TryDec(t.Get(r, "Prix"), out d) || d < 0) return "prix invalide pour « " + p.Name + " »";
            p.Price = d;
            string a = t.Get(r, "Actif");
            p.Active = a.Length == 0 || YesNo(a);
            return null;
        }

        public static string Match(string[] list, string value, string def)
        {
            string n = Fmt.Norm(value);
            foreach (string s in list) if (Fmt.Norm(s) == n) return s;
            return def;
        }
    }

    public class Client
    {
        public string Id = "";
        public string LastName = "";
        public string FirstName = "";
        public string Sex = "Homme";
        public int Age = 0;
        public decimal DistanceKm = 0;
        public string Notes = "";

        public string Label { get { return Id + " – " + LastName + " " + FirstName; } }
        public Client Clone() { return (Client)MemberwiseClone(); }

        public static readonly string[] Header = { "ID", "Nom", "Prenom", "Sexe", "Age", "DistanceKm", "Notes" };
        public string[] ToCsv() { return new[] { Id, LastName, FirstName, Sex, Age.ToString(), Fmt.Dec(DistanceKm), Notes }; }

        public static string FromCsv(CsvTable t, string[] r, out Client c)
        {
            c = new Client();
            c.Id = Fmt.Clean(t.Get(r, "ID"));
            c.LastName = Fmt.Clean(t.Get(r, "Nom"));
            c.FirstName = Fmt.Clean(t.Get(r, "Prenom"));
            if (c.LastName.Length == 0 && c.FirstName.Length == 0) return "client sans nom";
            c.Sex = Pack.Match(Lists.Sexes, t.Get(r, "Sexe"), "Homme");
            int a;
            if (!Fmt.TryInt(t.Get(r, "Age"), out a)) a = 0;
            c.Age = a;
            decimal d;
            if (!Fmt.TryDec(t.Get(r, "DistanceKm"), out d) || d < 0) d = 0;
            c.DistanceKm = d;
            c.Notes = Fmt.Clean(t.Get(r, "Notes"));
            return null;
        }
    }

    public class Purchase
    {
        public int Id;
        public string ClientId = "";
        public string PackName = "";
        public DateTime Start = DateTime.Today;
        public DateTime? End;
        public string EndReason = "";
        public int CarriedUsed = 0;   // crédits déjà consommés l'année précédente (report d'année)
        public string Note = "";

        public Purchase Clone() { return (Purchase)MemberwiseClone(); }

        public static readonly string[] Header = { "ID", "ClientID", "Pack", "DateDebut", "DateFin", "MotifFin", "CreditsDejaUtilises", "Note" };
        public string[] ToCsv() { return new[] { Id.ToString(), ClientId, PackName, Fmt.Date(Start), Fmt.Date(End), EndReason, CarriedUsed.ToString(), Note }; }

        public static string FromCsv(CsvTable t, string[] r, out Purchase p)
        {
            p = new Purchase();
            int i;
            if (!Fmt.TryInt(t.Get(r, "ID"), out i) || i <= 0) return "identifiant d'achat invalide";
            p.Id = i;
            p.ClientId = Fmt.Clean(t.Get(r, "ClientID"));
            p.PackName = Fmt.Clean(t.Get(r, "Pack"));
            if (p.ClientId.Length == 0 || p.PackName.Length == 0) return "achat sans client ou sans pack";
            DateTime d;
            if (!Fmt.TryDate(t.Get(r, "DateDebut"), out d)) return "date de début invalide (« " + t.Get(r, "DateDebut") + " »)";
            p.Start = d;
            string e = t.Get(r, "DateFin");
            if (e.Length > 0)
            {
                if (!Fmt.TryDate(e, out d)) return "date de fin invalide (« " + e + " »)";
                p.End = d;
            }
            p.EndReason = Fmt.Clean(t.Get(r, "MotifFin"));
            if (!Fmt.TryInt(t.Get(r, "CreditsDejaUtilises"), out i) || i < 0) i = 0;
            p.CarriedUsed = i;
            p.Note = Fmt.Clean(t.Get(r, "Note"));
            return null;
        }
    }

    public class Attendance
    {
        public int Id;
        public DateTime Date;
        public string Slot = "";
        public string Activity = "";
        public string ClientId = "";
        public int PurchaseId;   // 0 = aucun pack débité

        public static readonly string[] Header = { "ID", "Date", "Creneau", "Activite", "ClientID", "AchatID" };
        public string[] ToCsv() { return new[] { Id.ToString(), Fmt.Date(Date), Slot, Activity, ClientId, PurchaseId == 0 ? "" : PurchaseId.ToString() }; }

        public static string FromCsv(CsvTable t, string[] r, out Attendance a)
        {
            a = new Attendance();
            int i;
            if (!Fmt.TryInt(t.Get(r, "ID"), out i) || i <= 0) return "identifiant de présence invalide";
            a.Id = i;
            DateTime d;
            if (!Fmt.TryDate(t.Get(r, "Date"), out d)) return "date invalide (« " + t.Get(r, "Date") + " »)";
            a.Date = d;
            a.Slot = Fmt.Clean(t.Get(r, "Creneau"));
            a.Activity = Fmt.Clean(t.Get(r, "Activite"));
            a.ClientId = Fmt.Clean(t.Get(r, "ClientID"));
            if (a.Slot.Length == 0 || a.ClientId.Length == 0) return "présence sans créneau ou sans client";
            if (!Fmt.TryInt(t.Get(r, "AchatID"), out i) || i < 0) i = 0;
            a.PurchaseId = i;
            return null;
        }
    }

    public class Session
    {
        public DateTime Date;
        public string Slot = "";
        public string Activity = "";
    }

    public class DayInfo
    {
        public DateTime Date;
        public string Nature = "Normal";
    }

    public enum Sev { Ok = 0, Warning = 1, Error = 2 }

    public class Issue
    {
        public Sev Severity;
        public string Text;
        public Issue(Sev s, string t) { Severity = s; Text = t; }
    }
}
