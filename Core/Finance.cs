using System;
using System.Collections.Generic;
using System.Linq;

namespace GestionGym
{
    // ====================================================================== charges fixes
    public class Charge
    {
        public string Label = "";
        public string Category = "Autres";
        public decimal Amount = 0;
        public string Frequency = "Mensuel";
        public DateTime Start = DateTime.Today;
        public DateTime? End;

        public Charge Clone() { return (Charge)MemberwiseClone(); }

        public static readonly string[] Header = { "Libelle", "Categorie", "Montant", "Frequence", "Debut", "Fin" };
        public string[] ToCsv() { return new[] { Label, Category, Fmt.Dec(Amount), Frequency, Fmt.Date(Start), Fmt.Date(End) }; }

        public static string FromCsv(CsvTable t, string[] r, out Charge c)
        {
            c = new Charge();
            c.Label = Fmt.Clean(t.Get(r, "Libelle"));
            if (c.Label.Length == 0) return "charge sans libellé";
            c.Category = Pack.Match(Fld.ChargeCats, t.Get(r, "Categorie"), "Autres");
            decimal a;
            if (!Fmt.TryDec(t.Get(r, "Montant"), out a) || a < 0) return "montant de charge invalide";
            c.Amount = a;
            c.Frequency = Pack.Match(Fld.Frequences, t.Get(r, "Frequence"), "Mensuel");
            DateTime d;
            if (!Fmt.TryDate(t.Get(r, "Debut"), out d)) return "date de début de charge invalide";
            c.Start = d;
            DateTime e;
            string fin = t.Get(r, "Fin");
            c.End = (fin.Trim().Length > 0 && Fmt.TryDate(fin, out e)) ? (DateTime?)e : null;
            return null;
        }

        public int Step
        {
            get
            {
                switch (Frequency)
                {
                    case "Mensuel": return 1;
                    case "Trimestriel": return 3;
                    case "Semestriel": return 6;
                    case "Annuel": return 12;
                    default: return 0;
                }
            }
        }

        /// <summary>Montant dû pour le mois m (1-12) de l'année : mensuel = chaque mois dès le début ; trimestriel/semestriel/annuel = tous les 3/6/12 mois ; ponctuel = le mois du début.</summary>
        public decimal AmountFor(int year, int m)
        {
            int diff = (year - Start.Year) * 12 + m - Start.Month;
            if (diff < 0) return 0;
            int step = Step;
            if (step == 0 ? diff != 0 : diff % step != 0) return 0;
            if (End.HasValue && new DateTime(year, m, 1) > End.Value) return 0;
            return Amount;
        }

        public decimal YearTotal(int year)
        {
            decimal t = 0;
            for (int m = 1; m <= 12; m++) t += AmountFor(year, m);
            return t;
        }
    }

    // ====================================================================== lignes de résultat
    public class KV
    {
        public string Label;
        public string Value;
        public int Level;       // 0 normal, 1 avertissement, 2 important (gras)
        public KV(string l, string v, int level = 0) { Label = l; Value = v; Level = level; }
    }

    public class MonthRow
    {
        public decimal Ca, Cot, Acre, Cfp, Total, Vl, Charges, ResAv;
    }

    public class UrssafResult
    {
        public bool Micro;
        public decimal CaAuto, Ca, Abat, TCot, TCfp, Plaf, TvaS, TvaM, VlR, Parts, AcreR;
        public DateTime? AcreF;
        public bool VlOk;
        public MonthRow[] Months = new MonthRow[12];
        public MonthRow Total = new MonthRow();
        public decimal TauxEff, ResAv, ImpMicro, ImpReel, Rev, Qf, IrAvec, IrSans, IrAct, Impot, Net, NetPct, Seuil, PlafR;
        public string Conseil, AlPlaf, AlTva, AlStat;
        public decimal[] Charges = new decimal[12];

        public List<KV> Lines()
        {
            var l = new List<KV>();
            l.Add(new KV("CA auto-calculé (packs + autres recettes)", Fmt.Money(CaAuto)));
            l.Add(new KV("CA retenu pour le calcul (après remises)", Fmt.Money(Ca), 2));
            l.Add(new KV("Abattement forfaitaire (impôt micro)", Fmt.Dec(Abat * 100) + " %"));
            l.Add(new KV("Taux de cotisations sociales", Fmt.Dec(TCot * 100) + " %"));
            l.Add(new KV("Taux de contribution formation pro", Fmt.Dec(TCfp * 100) + " %"));
            l.Add(new KV("Plafond de CA de la catégorie", Fmt.Money0(Plaf)));
            l.Add(new KV("Nombre de parts fiscales", Parts.ToString("0.0", Fmt.Fr)));
            l.Add(new KV("Réduction ACRE applicable", Fmt.Dec(AcreR * 100) + " %" + (AcreF.HasValue && AcreR > 0 ? " jusqu'au " + Fmt.Date(AcreF) : "")));
            l.Add(new KV("Versement libératoire applicable ?", VlOk ? "Oui" : "Non"));
            l.Add(new KV("Cotisations sociales (avant ACRE)", Fmt.Money(Total.Cot)));
            l.Add(new KV("Réduction ACRE", Fmt.Money(Total.Acre)));
            l.Add(new KV("Contribution formation professionnelle", Fmt.Money(Total.Cfp)));
            l.Add(new KV("TOTAL cotisations URSSAF", Fmt.Money(Total.Total), 2));
            l.Add(new KV("Taux de cotisation effectif", Fmt.Dec(TauxEff * 100) + " %"));
            l.Add(new KV("Charges fixes de l'année", Fmt.Money(Total.Charges)));
            l.Add(new KV("Résultat avant impôt (CA − cotisations − charges)", Fmt.Money(ResAv), 2));
            l.Add(new KV("Revenu imposable (micro, après abattement)", Fmt.Money(ImpMicro)));
            l.Add(new KV("Revenu imposable (réel, indicatif)", Fmt.Money(ImpReel)));
            l.Add(new KV("Régime d'imposition le plus favorable", Conseil));
            l.Add(new KV("Impôt supplémentaire dû à l'activité (barème simplifié)", Fmt.Money(IrAct)));
            l.Add(new KV("Versement libératoire payé", Fmt.Money(Total.Vl)));
            l.Add(new KV("Impôt retenu dans le résultat net", Fmt.Money(Impot)));
            l.Add(new KV("RÉSULTAT NET (après cotisations, charges, impôt)", Fmt.Money(Net), 2));
            l.Add(new KV("Résultat net en % du CA", Fmt.Dec(NetPct * 100) + " %"));
            l.Add(new KV("Seuil de rentabilité : CA mensuel pour couvrir les charges", Fmt.Money(Seuil)));
            l.Add(new KV("Marge restante avant le plafond de CA", Fmt.Money0(PlafR)));
            l.Add(new KV("Alerte plafond micro", AlPlaf, AlPlaf.StartsWith("⚠") ? 1 : 0));
            l.Add(new KV("Alerte seuil de TVA", AlTva, AlTva.StartsWith("⚠") ? 1 : 0));
            l.Add(new KV("Remarque sur le statut", AlStat));
            return l;
        }
    }

    public class CfeResult
    {
        public int? AnDeb;
        public decimal Base, Reduc, BaseR, Brute, Fch, Fge, Total, Acompte, Solde, PctCa, Ca;
        public string ExoMotif, Fourchette, Alerte;
        public decimal[] Months = new decimal[12];

        public List<KV> Lines()
        {
            var l = new List<KV>();
            l.Add(new KV("Année de création retenue", AnDeb.HasValue ? AnDeb.Value.ToString() : "(non renseignée)"));
            l.Add(new KV("Base d'imposition (max valeur locative, base minimale)", Fmt.Money(Base)));
            l.Add(new KV("Réduction de base (2e année)", Fmt.Money(Reduc)));
            l.Add(new KV("Base retenue", Fmt.Money(BaseR)));
            l.Add(new KV("Exonération", ExoMotif));
            l.Add(new KV("CFE brute (base × taux)", Fmt.Money(Brute)));
            l.Add(new KV("Frais de chambre", Fmt.Money(Fch)));
            l.Add(new KV("Frais de gestion", Fmt.Money(Fge)));
            l.Add(new KV("CFE TOTALE DE L'ANNÉE", Fmt.Money(Total), 2));
            l.Add(new KV("Acompte (15 juin)", Fmt.Money(Acompte)));
            l.Add(new KV("Solde (15 décembre)", Fmt.Money(Solde)));
            l.Add(new KV("CFE en % du CA", Ca > 0 ? Fmt.Dec(PctCa * 100) + " %" : ""));
            l.Add(new KV("Fourchette de base minimale pour ce CA", Fourchette));
            l.Add(new KV("Alerte", Alerte, Alerte.StartsWith("⚠") ? 1 : 0));
            return l;
        }
    }

    public class ClientProfit
    {
        public Client Client;
        public string AgeBand = "", DistBand = "", Pack = "", Status = "", Slot = "", Activity = "";
        public decimal Revenue, FixedShare, Contrib, Margin, MarginPct, RevPerPres, DiscountPct, DiscountEur;
        public int Presences, Rank, AncMonths;
        public double PerMonth;
        public bool IsNew, Loyal;
    }

    public class Segment
    {
        public string Name = "";
        public int Clients;
        public decimal Revenue, Margin;
        public int Presences;
        public decimal AvgRevenue { get { return Clients == 0 ? 0 : Revenue / Clients; } }
        public decimal AvgMargin { get { return Clients == 0 ? 0 : Margin / Clients; } }
    }

    public class RentaResult
    {
        public List<ClientProfit> Clients = new List<ClientProfit>();
        public decimal Ca, Charges, Contrib, ResAv, AvgRevenue, FixedPerPres, BreakEven, CoverRatio, DiscountCost;
        public int PayingClients, LoyalCount, NewCount;
        public decimal RevNew, RevLoyal, BudgetB, BudgetC, PctA, PctB, PctC, CostA, CostB, CostC, Advised;
        public readonly Dictionary<string, List<Segment>> Segments = new Dictionary<string, List<Segment>>();

        public List<KV> Lines()
        {
            var l = new List<KV>();
            l.Add(new KV("CA des packs de l'année", Fmt.Money0(Ca)));
            l.Add(new KV("Charges fixes de l'année", Fmt.Money0(Charges)));
            l.Add(new KV("Cotisations URSSAF estimées (taux effectif)", Fmt.Money0(Contrib)));
            l.Add(new KV("Résultat avant impôt (packs)", Fmt.Money0(ResAv), 2));
            l.Add(new KV("Clients avec revenu", PayingClients.ToString()));
            l.Add(new KV("Revenu moyen par client", Fmt.Money(AvgRevenue)));
            l.Add(new KV("Coût fixe imputé par présence", Fmt.Money(FixedPerPres)));
            l.Add(new KV("Seuil de rentabilité annuel (CA)", Fmt.Money0(BreakEven)));
            l.Add(new KV("CA réalisé ÷ seuil de rentabilité", BreakEven > 0 ? Fmt.Dec(CoverRatio * 100) + " %" : ""));
            l.Add(new KV("Clients fidèles éligibles", LoyalCount.ToString()));
            l.Add(new KV("Nouveaux inscrits de l'année", NewCount.ToString()));
            l.Add(new KV("Coût annuel des remises (méthode individuelle)", Fmt.Money0(DiscountCost)));
            l.Add(new KV("A. Remise moyenne – individuelle", Fmt.Dec(PctA * 100) + " %   (coût " + Fmt.Money0(CostA) + ")"));
            l.Add(new KV("B. Remise – financée par les nouveaux inscrits", Fmt.Dec(PctB * 100) + " %   (budget " + Fmt.Money0(BudgetB) + ", coût " + Fmt.Money0(CostB) + ")"));
            l.Add(new KV("C. Remise – financée par les autres clients", Fmt.Dec(PctC * 100) + " %   (budget " + Fmt.Money0(BudgetC) + ", coût " + Fmt.Money0(CostC) + ")"));
            l.Add(new KV("REMISE CONSEILLÉE (prudente : la plus faible des 3)", Fmt.Dec(Advised * 100) + " %", 2));
            return l;
        }
    }

    // ====================================================================== calculs
    public static class Finance
    {
        static decimal Min(decimal a, decimal b) { return a < b ? a : b; }
        static decimal Max(decimal a, decimal b) { return a > b ? a : b; }

        public static decimal[] ChargesByMonth(Database db)
        {
            var r = new decimal[12];
            foreach (Charge c in db.Charges)
                for (int m = 1; m <= 12; m++) r[m - 1] += c.AmountFor(db.Year, m);
            return r;
        }

        public static Dictionary<string, decimal> ChargesByCategory(Database db)
        {
            var d = new Dictionary<string, decimal>();
            foreach (string c in Fld.ChargeCats) d[c] = 0;
            foreach (Charge c in db.Charges)
            {
                decimal v;
                d.TryGetValue(c.Category, out v);
                d[c.Category] = v + c.YearTotal(db.Year);
            }
            return d;
        }

        // ------------------------------------------------------------------ URSSAF
        public static UrssafResult Urssaf(Database db)
        {
            var u = new UrssafResult();
            string statut = Fld.Str(db, "U.statut");
            string activite = Fld.Str(db, "U.activite");
            int ci = Math.Max(0, Array.IndexOf(Fld.Cats, activite));
            u.Micro = statut == "Micro-entrepreneur";
            bool declared = Fld.Str(db, "U.mode") == "Déclaré";
            decimal caDecl = Fld.Dec(db, "U.caDecl");
            decimal autresRec = Fld.Dec(db, "U.autresRec");
            decimal remises = Fld.Dec(db, "U.remises");
            DateTime? debut = Fld.Date(db, "U.debut");

            decimal[] rev = db.Calc.RevenueByMonth();
            u.CaAuto = rev.Sum() + autresRec;
            u.Ca = Max(0, (declared ? caDecl : u.CaAuto) - remises);
            string p = "P." + ci + ".";
            u.Abat = Fld.Frac(db, p + "abat");
            u.TCot = Fld.Frac(db, p + "cot");
            u.TCfp = Fld.Frac(db, p + "cfp");
            u.Plaf = Fld.Dec(db, p + "plaf");
            u.TvaS = Fld.Dec(db, p + "tvaS");
            u.TvaM = Fld.Dec(db, p + "tvaM");
            u.VlR = Fld.Frac(db, p + "vl");
            int enf = Fld.Int(db, "U.enf");
            u.Parts = (Fld.Str(db, "U.fam").StartsWith("Marié") ? 2m : 1m) + (enf <= 2 ? 0.5m * enf : enf - 1);
            u.AcreR = 0;
            if (Fld.Yes(db, "U.acre") && debut.HasValue)
            {
                DateTime lim = Fld.Date(db, "P.acreLim") ?? new DateTime(2026, 6, 30);
                u.AcreR = debut.Value <= lim ? Fld.Frac(db, "P.acre1") : Fld.Frac(db, "P.acre2");
                int qEnd = 3 * (int)Math.Ceiling(debut.Value.Month / 3.0);
                DateTime f = new DateTime(debut.Value.Year, qEnd, 1).AddMonths(9);
                u.AcreF = new DateTime(f.Year, f.Month, DateTime.DaysInMonth(f.Year, f.Month));
            }
            decimal rfr = Fld.Dec(db, "U.rfr");
            u.VlOk = Fld.Yes(db, "U.vl") && u.Micro && rfr <= Fld.Dec(db, "P.rfrPart") * u.Parts;
            decimal tns = Fld.Frac(db, "P.tns");
            bool chInc = Fld.Yes(db, "U.chInc");
            u.Charges = ChargesByMonth(db);

            for (int m = 1; m <= 12; m++)
            {
                var r = new MonthRow();
                r.Ca = declared ? u.Ca / 12m : Max(0, rev[m - 1] + (autresRec - remises) / 12m);
                r.Charges = chInc ? u.Charges[m - 1] : 0;
                r.Cot = u.Micro ? r.Ca * u.TCot : Max(0, r.Ca - r.Charges) * tns;
                DateTime first = new DateTime(db.Year, m, 1);
                DateTime last = new DateTime(db.Year, m, DateTime.DaysInMonth(db.Year, m));
                bool inAcre = u.Micro && u.AcreR > 0 && debut.HasValue && u.AcreF.HasValue && last >= debut.Value && first <= u.AcreF.Value;
                r.Acre = inAcre ? -r.Cot * u.AcreR : 0;
                r.Cfp = u.Micro ? r.Ca * u.TCfp : 0;
                r.Total = r.Cot + r.Acre + r.Cfp;
                r.Vl = u.VlOk ? r.Ca * u.VlR : 0;
                r.ResAv = r.Ca - r.Total - r.Charges;
                u.Months[m - 1] = r;
                u.Total.Ca += r.Ca; u.Total.Cot += r.Cot; u.Total.Acre += r.Acre; u.Total.Cfp += r.Cfp;
                u.Total.Total += r.Total; u.Total.Vl += r.Vl; u.Total.Charges += r.Charges; u.Total.ResAv += r.ResAv;
            }
            u.TauxEff = u.Ca == 0 ? 0 : u.Total.Total / u.Ca;
            u.ResAv = u.Ca - u.Total.Total - u.Total.Charges;
            u.ImpMicro = u.Ca == 0 ? 0 : u.Ca - Max(u.Ca * u.Abat, 305m);
            u.ImpReel = Max(0, u.Ca - u.Total.Total - u.Total.Charges);
            u.Conseil = u.ImpReel < u.ImpMicro ? "Réel : charges > abattement" : "Micro : abattement forfaitaire";
            decimal autres = Fld.Dec(db, "U.autres");
            u.Rev = (u.Micro ? u.ImpMicro : u.ImpReel) + autres;
            u.Qf = u.Rev / u.Parts;
            u.IrAvec = Bareme(db, u.Qf) * u.Parts;
            u.IrSans = Bareme(db, autres / u.Parts) * u.Parts;
            u.IrAct = Max(0, u.IrAvec - u.IrSans);
            u.Impot = u.VlOk ? u.Total.Vl : u.IrAct;
            u.Net = u.Ca - u.Total.Total - u.Total.Charges - u.Impot;
            u.NetPct = u.Ca == 0 ? 0 : u.Net / u.Ca;
            u.Seuil = u.TauxEff >= 1 ? 0 : (u.Total.Charges / 12m) / (1 - u.TauxEff);
            u.PlafR = u.Plaf - u.Ca;
            u.AlPlaf = !u.Micro ? "Non concerné (hors micro)" : (u.Ca > u.Plaf ? "⚠ Plafond dépassé" : (u.Ca > 0.9m * u.Plaf ? "⚠ Proche du plafond" : "OK"));
            u.AlTva = Fld.Str(db, "U.tva") == "Assujetti à la TVA" ? "Assujetti" : (u.Ca > u.TvaM ? "⚠ Seuil majoré dépassé : TVA dès maintenant" : (u.Ca > u.TvaS ? "⚠ Seuil de base dépassé" : "OK"));
            u.AlStat = u.Micro ? "Calcul micro-entrepreneur" : "Calcul INDICATIF : cotisations = taux TNS × (CA − charges)";
            return u;
        }

        /// <summary>Barème progressif par part (calcul simplifié par incréments).</summary>
        static decimal Bareme(Database db, decimal qf)
        {
            decimal total = 0, prevRate = 0;
            for (int i = 0; i < 5; i++)
            {
                decimal lo = Fld.Dec(db, "B." + i + ".lo");
                decimal tx = Fld.Frac(db, "B." + i + ".tx");
                decimal inc = tx - prevRate;
                if (qf > lo && inc > 0) total += (qf - lo) * inc;
                prevRate = tx;
            }
            return total;
        }

        // ------------------------------------------------------------------ CFE
        public static CfeResult Cfe(Database db, UrssafResult urssaf)
        {
            var c = new CfeResult();
            int? input = null;
            if (Fld.Str(db, "C.debut").Length > 0) input = Fld.Int(db, "C.debut");
            DateTime? ud = Fld.Date(db, "U.debut");
            c.AnDeb = input ?? (ud.HasValue ? (int?)ud.Value.Year : null);
            bool local = Fld.Str(db, "C.local") == Fld.Locaux[0];
            decimal vl = Fld.Dec(db, "C.vl"), bmin = Fld.Dec(db, "C.bmin"), taux = Fld.Frac(db, "C.taux"), ca2 = Fld.Dec(db, "C.ca2");
            c.Ca = urssaf != null ? urssaf.Ca : 0;
            c.Base = local ? Max(vl, bmin) : bmin;
            c.Reduc = (c.AnDeb.HasValue && db.Year == c.AnDeb.Value + 1) ? c.Base * Fld.Frac(db, "CP.reduc") : 0;
            c.BaseR = c.Base - c.Reduc;
            bool exoCreation = c.AnDeb.HasValue && db.Year == c.AnDeb.Value;
            if (exoCreation) c.ExoMotif = "Exonérée : année de création";
            else if (Fld.Yes(db, "C.exo")) c.ExoMotif = "Exonération particulière";
            else if (!local && ca2 <= Fld.Dec(db, "CP.exo") && ca2 > 0) c.ExoMotif = "Exonérée : CA N-2 ≤ seuil";
            else c.ExoMotif = "Non exonérée";
            c.Brute = c.ExoMotif.StartsWith("Exo") ? 0 : c.BaseR * taux;
            c.Fch = c.Brute * Fld.Frac(db, "C.chambre");
            c.Fge = c.Brute * Fld.Frac(db, "C.gestion");
            c.Total = c.Brute + c.Fch + c.Fge;
            decimal prev = Fld.Dec(db, "C.prev");
            c.Acompte = prev >= Fld.Dec(db, "CP.seuilAc") ? prev * Fld.Frac(db, "CP.partAc") : 0;
            c.Solde = c.Total - c.Acompte;
            c.PctCa = c.Ca > 0 ? c.Total / c.Ca : 0;
            int t = 5;
            for (int i = 0; i < 6; i++) if (ca2 <= Fld.Dec(db, "CB." + i + ".max")) { t = i; break; }
            decimal lo = Fld.Dec(db, "CB." + t + ".min"), hi = Fld.Dec(db, "CB." + t + ".mx");
            c.Fourchette = Fmt.Dec(lo) + " à " + Fmt.Dec(hi) + " €";
            if (!local && (bmin < lo || bmin > hi)) c.Alerte = "⚠ Base minimale hors fourchette indicative";
            else if (taux >= 1) c.Alerte = "⚠ Vérifie le taux (en %, ex. 25)";
            else c.Alerte = "OK";
            c.Months[5] = c.Acompte;
            c.Months[11] = c.Solde;
            return c;
        }

        // ------------------------------------------------------------------ rentabilité par client
        public static string AgeBand(int a)
        {
            if (a <= 0) return "";
            if (a < 18) return "Moins de 18 ans";
            if (a <= 25) return "18 à 25 ans";
            if (a <= 35) return "26 à 35 ans";
            if (a <= 45) return "36 à 45 ans";
            if (a <= 55) return "46 à 55 ans";
            return "56 ans et plus";
        }

        public static string DistBand(decimal d)
        {
            if (d <= 0) return "";
            if (d <= 2) return "0 à 2 km";
            if (d <= 5) return "2 à 5 km";
            if (d <= 10) return "5 à 10 km";
            if (d <= 20) return "10 à 20 km";
            return "Plus de 20 km";
        }

        public static readonly string[] AgeBands = { "Moins de 18 ans", "18 à 25 ans", "26 à 35 ans", "36 à 45 ans", "46 à 55 ans", "56 ans et plus" };
        public static readonly string[] DistBands = { "0 à 2 km", "2 à 5 km", "5 à 10 km", "10 à 20 km", "Plus de 20 km" };

        public static RentaResult Renta(Database db, UrssafResult urssaf)
        {
            var R = new RentaResult();
            Calc calc = db.Calc;
            DateTime yearStart = new DateTime(db.Year, 1, 1), yearEnd = new DateTime(db.Year, 12, 31);
            DateTime asOf = db.AsOf < yearEnd ? db.AsOf : yearEnd;
            R.Ca = db.Purchases.Sum(p => calc.YearRevenue(p));
            R.Charges = urssaf.Total.Charges;
            decimal rate = urssaf.TauxEff;
            R.Contrib = R.Ca * rate;
            R.ResAv = R.Ca - R.Charges - R.Contrib;
            int totalPres = db.Attendances.Count(a => a.Date.Year == db.Year);
            R.FixedPerPres = totalPres == 0 ? 0 : R.Charges / totalPres;
            R.BreakEven = rate >= 1 ? 0 : R.Charges / (1 - rate);
            R.CoverRatio = R.BreakEven == 0 ? 0 : R.Ca / R.BreakEven;
            decimal fidMax = Fld.Frac(db, "R.fidMax"), margeMin = Fld.Frac(db, "R.margeMin");
            int ancMin = Fld.Int(db, "R.ancMin");
            double assMin = (double)Fld.Dec(db, "R.assMin");

            foreach (Client c in db.Clients)
            {
                var cp = new ClientProfit { Client = c };
                var mine = db.Purchases.Where(p => p.ClientId == c.Id).ToList();
                Purchase main;
                cp.Status = calc.ClientStatus(c, out main);
                cp.Pack = main != null ? main.PackName : "";
                cp.AgeBand = AgeBand(c.Age);
                cp.DistBand = DistBand(c.DistanceKm);
                cp.Slot = calc.FavoriteSlot(c.Id) ?? "";
                cp.Activity = calc.FavoriteActivity(c.Id) ?? "";
                cp.Revenue = mine.Sum(p => calc.YearRevenue(p));
                int pres; calc.PresencesByClient.TryGetValue(c.Id, out pres);
                cp.Presences = pres;
                cp.FixedShare = cp.Presences * R.FixedPerPres;
                cp.Contrib = cp.Revenue * rate;
                cp.Margin = cp.Revenue - cp.FixedShare - cp.Contrib;
                cp.MarginPct = cp.Revenue == 0 ? 0 : cp.Margin / cp.Revenue;
                cp.RevPerPres = cp.Presences == 0 ? 0 : cp.Revenue / cp.Presences;
                if (mine.Count > 0)
                {
                    DateTime first = mine.Min(p => p.Start);
                    cp.AncMonths = first > asOf ? 0 : Fmt.MonthsBetween(first, asOf);
                    cp.IsNew = first.Year == db.Year;
                    int months = 0;
                    for (int m = 1; m <= 12; m++)
                    {
                        DateTime ms = new DateTime(db.Year, m, 1), me = new DateTime(db.Year, m, DateTime.DaysInMonth(db.Year, m));
                        if (mine.Any(p => p.Start <= me && (calc.EffectiveEnd(p) ?? DateTime.MaxValue) >= ms)) months++;
                    }
                    cp.PerMonth = cp.Presences / (double)Math.Max(1, months);
                }
                cp.Loyal = cp.Status == "Abonné" && cp.AncMonths >= ancMin && cp.PerMonth >= assMin && cp.Margin > 0;
                if (cp.Loyal)
                {
                    double score = 0.5 * Math.Min(1.0, cp.AncMonths / (2.0 * Math.Max(1, ancMin))) + 0.5 * Math.Min(1.0, cp.PerMonth / (2.0 * Math.Max(0.1, assMin)));
                    decimal byScore = fidMax * (decimal)score;
                    decimal cap = Max(0, cp.MarginPct - margeMin);
                    cp.DiscountPct = Min(byScore, cap);
                }
                cp.DiscountEur = cp.Revenue * cp.DiscountPct;
                R.Clients.Add(cp);
            }
            // rang par marge (égalité : ordre de la liste)
            var ranked = R.Clients.Select((x, i) => new { x, i }).OrderByDescending(t => t.x.Margin).ThenBy(t => t.i).ToList();
            for (int i = 0; i < ranked.Count; i++) ranked[i].x.Rank = i + 1;

            R.PayingClients = R.Clients.Count(c => c.Revenue > 0);
            R.AvgRevenue = R.PayingClients == 0 ? 0 : R.Ca / R.PayingClients;
            R.LoyalCount = R.Clients.Count(c => c.Loyal);
            R.NewCount = R.Clients.Count(c => c.IsNew);
            R.DiscountCost = R.Clients.Sum(c => c.DiscountEur);
            R.RevNew = R.Clients.Where(c => c.IsNew).Sum(c => c.Revenue);
            R.RevLoyal = R.Clients.Where(c => c.Loyal).Sum(c => c.Revenue);
            decimal revOthers = R.Clients.Where(c => !c.Loyal).Sum(c => c.Revenue);
            R.PctA = R.RevLoyal == 0 ? 0 : R.DiscountCost / R.RevLoyal;
            R.CostA = R.DiscountCost;
            R.BudgetB = Fld.Frac(db, "R.reinvNew") * R.RevNew;
            R.PctB = R.RevLoyal == 0 ? 0 : Min(fidMax, R.BudgetB / R.RevLoyal);
            R.CostB = R.PctB * R.RevLoyal;
            R.BudgetC = Fld.Frac(db, "R.reinvOth") * revOthers;
            R.PctC = R.RevLoyal == 0 ? 0 : Min(fidMax, R.BudgetC / R.RevLoyal);
            R.CostC = R.PctC * R.RevLoyal;
            R.Advised = Min(R.PctA, Min(R.PctB, R.PctC));

            AddSegments(R, "Tranche d'âge", Finance.AgeBands, c => c.AgeBand);
            AddSegments(R, "Tranche de distance", Finance.DistBands, c => c.DistBand);
            AddSegments(R, "Sexe", Lists.Sexes, c => c.Client.Sex);
            AddSegments(R, "Statut", new[] { "Abonné", "Pack actif", "Inactif", "Sans pack" }, c => c.Status);
            AddSegments(R, "Activité préférée", db.Activities.ToArray(), c => c.Activity);
            AddSegments(R, "Pack", db.Packs.Select(p => p.Name).ToArray(), c => c.Pack);
            return R;
        }

        static void AddSegments(RentaResult R, string title, string[] cats, Func<ClientProfit, string> key)
        {
            var l = new List<Segment>();
            foreach (string cat in cats)
            {
                var m = R.Clients.Where(c => string.Equals(key(c), cat, StringComparison.OrdinalIgnoreCase)).ToList();
                l.Add(new Segment { Name = cat, Clients = m.Count, Revenue = m.Sum(c => c.Revenue), Margin = m.Sum(c => c.Margin), Presences = m.Sum(c => c.Presences) });
            }
            R.Segments[title] = l;
        }
    }
}
