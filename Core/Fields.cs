using System;
using System.Collections.Generic;
using System.Linq;

namespace GestionGym
{
    public enum FKind { Text, Int, Dec, Pct, Money, Date, List }

    /// <summary>Description d'un paramètre enregistré dans parametres.csv (profil URSSAF, CFE, taux, rentabilité…).</summary>
    public class FieldDef
    {
        public string Key;
        public string Label;
        public FKind Kind;
        public string[] Options;
        public string Default = "";
        public decimal Min = 0;
        public decimal Max = 1000000000m;
        public bool Optional;       // la valeur peut rester vide (dates)
        public string Help = "";
        public string Group = "";

        public FieldDef(string key, string label, FKind kind, string def, string help = "")
        {
            Key = key; Label = label; Kind = kind; Default = def; Help = help;
            if (kind == FKind.Pct) Max = 100;
        }
        public FieldDef Opts(params string[] o) { Options = o; return this; }
        public FieldDef Range(decimal min, decimal max) { Min = min; Max = max; return this; }
        public FieldDef Opt() { Optional = true; return this; }
    }

    /// <summary>Registre et accès typés (avec valeurs par défaut) aux paramètres fiscaux et de rentabilité.</summary>
    public static class Fld
    {
        public static readonly string[] Cats = { "Vente de marchandises (BIC)", "Prestation de services (BIC)", "Profession libérale – SSI (BNC)", "Profession libérale – CIPAV (BNC)" };
        public static readonly string[] Statuts = { "Micro-entrepreneur", "EI au réel (indicatif)", "EURL / SASU / SARL (indicatif)", "Autre (indicatif)" };
        public static readonly string[] Familles = { "Célibataire", "Marié(e) / Pacsé(e)", "Divorcé(e) / Séparé(e)", "Veuf / Veuve" };
        public static readonly string[] ChargeCats = { "Loyer et charges locatives", "Énergie (électricité, eau, gaz)", "Assurances", "Matériel et équipement", "Entretien et ménage", "Salaires / prestataires", "Marketing et communication", "Logiciels et abonnements", "Banque et comptabilité", "Taxes (CFE, etc.)", "Autres" };
        public static readonly string[] Frequences = { "Mensuel", "Trimestriel", "Semestriel", "Annuel", "Ponctuel" };
        public static readonly string[] Locaux = { "Local professionnel dédié", "Sans local (domicile)" };
        public static readonly string[] OuiNon = { "Oui", "Non" };

        public static readonly List<FieldDef> Urssaf = new List<FieldDef>();
        public static readonly List<FieldDef> UrssafParams = new List<FieldDef>();
        public static readonly List<FieldDef> Cfe = new List<FieldDef>();
        public static readonly List<FieldDef> CfeParams = new List<FieldDef>();
        public static readonly List<FieldDef> Renta = new List<FieldDef>();
        static readonly Dictionary<string, FieldDef> all = new Dictionary<string, FieldDef>(StringComparer.OrdinalIgnoreCase);

        static Fld()
        {
            // ---- profil URSSAF
            Urssaf.Add(new FieldDef("U.statut", "Statut juridique", FKind.List, Statuts[0]).Opts(Statuts));
            Urssaf.Add(new FieldDef("U.activite", "Nature de l'activité", FKind.List, Cats[1]).Opts(Cats));
            Urssaf.Add(new FieldDef("U.mode", "Chiffre d'affaires : auto-calculé ou déclaré ?", FKind.List, "Auto-calculé", "Auto-calculé = encaissements des packs de la base").Opts("Auto-calculé", "Déclaré"));
            Urssaf.Add(new FieldDef("U.caDecl", "CA annuel déclaré (si « Déclaré ») (€)", FKind.Money, "0"));
            Urssaf.Add(new FieldDef("U.debut", "Date de début d'activité", FKind.Date, "").Opt());
            Urssaf.Add(new FieldDef("U.acre", "Bénéficiaire de l'ACRE ?", FKind.List, "Non").Opts(OuiNon));
            Urssaf.Add(new FieldDef("U.vl", "Option versement libératoire de l'impôt ?", FKind.List, "Non").Opts(OuiNon));
            Urssaf.Add(new FieldDef("U.period", "Périodicité des déclarations", FKind.List, "Mensuelle").Opts("Mensuelle", "Trimestrielle"));
            Urssaf.Add(new FieldDef("U.fam", "Situation familiale", FKind.List, Familles[0]).Opts(Familles));
            Urssaf.Add(new FieldDef("U.enf", "Nombre d'enfants à charge", FKind.Int, "0").Range(0, 20));
            Urssaf.Add(new FieldDef("U.autres", "Autres revenus nets imposables du foyer (€/an)", FKind.Money, "0"));
            Urssaf.Add(new FieldDef("U.rfr", "Revenu fiscal de référence N-2 du foyer (€)", FKind.Money, "0"));
            Urssaf.Add(new FieldDef("U.chInc", "Déduire les charges fixes du résultat ?", FKind.List, "Oui").Opts(OuiNon));
            Urssaf.Add(new FieldDef("U.tva", "Régime de TVA", FKind.List, "Franchise en base").Opts("Franchise en base", "Assujetti à la TVA"));
            Urssaf.Add(new FieldDef("U.autresRec", "Autres recettes annuelles hors packs (€)", FKind.Money, "0"));
            Urssaf.Add(new FieldDef("U.remises", "Remises / avoirs accordés dans l'année (€)", FKind.Money, "0"));

            // ---- paramètres URSSAF (taux 2026 – à vérifier)
            decimal[][] prm =
            {
                new[] { 12.3m, 0.1m, 71m, 1m, 203100m, 85000m, 93500m },
                new[] { 21.2m, 0.1m, 50m, 1.7m, 83600m, 37500m, 41250m },
                new[] { 25.6m, 0.2m, 34m, 2.2m, 83600m, 37500m, 41250m },
                new[] { 23.2m, 0.2m, 34m, 2.2m, 83600m, 37500m, 41250m }
            };
            string[] pn = { "cotisations sociales (%)", "contribution formation pro (%)", "abattement impôt micro (%)", "versement libératoire (%)", "plafond de CA (€)", "seuil TVA (€)", "seuil TVA majoré (€)" };
            string[] pk = { "cot", "cfp", "abat", "vl", "plaf", "tvaS", "tvaM" };
            FKind[] pt = { FKind.Pct, FKind.Pct, FKind.Pct, FKind.Pct, FKind.Money, FKind.Money, FKind.Money };
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 7; j++)
                {
                    var f = new FieldDef("P." + i + "." + pk[j], Cats[i] + " – " + pn[j], pt[j], Fmt.Dec(prm[i][j]));
                    f.Group = Cats[i];
                    UrssafParams.Add(f);
                }
            UrssafParams.Add(new FieldDef("P.acre1", "ACRE – réduction si début d'activité ≤ date limite (%)", FKind.Pct, "50"));
            UrssafParams.Add(new FieldDef("P.acre2", "ACRE – réduction si début d'activité après la date limite (%)", FKind.Pct, "25"));
            UrssafParams.Add(new FieldDef("P.acreLim", "ACRE – date limite du taux majoré", FKind.Date, "30/06/2026"));
            UrssafParams.Add(new FieldDef("P.rfrPart", "Plafond de RFR N-2 par part (versement libératoire) (€)", FKind.Money, "29579"));
            UrssafParams.Add(new FieldDef("P.tns", "Taux de cotisations TNS indicatif hors micro (%)", FKind.Pct, "45"));
            decimal[] blo = { 0, 11600, 29579, 84577, 181917 };
            decimal[] btx = { 0, 11, 30, 41, 45 };
            for (int i = 0; i < 5; i++)
            {
                UrssafParams.Add(new FieldDef("B." + i + ".lo", "Barème IR – tranche " + (i + 1) + " : à partir de (€ / part)", FKind.Money, Fmt.Dec(blo[i])));
                UrssafParams.Add(new FieldDef("B." + i + ".tx", "Barème IR – tranche " + (i + 1) + " : taux (%)", FKind.Pct, Fmt.Dec(btx[i])));
            }

            // ---- profil CFE
            Cfe.Add(new FieldDef("C.debut", "Année de création de l'activité (vide = année du début d'activité URSSAF)", FKind.Int, "").Range(1990, 2200).Opt());
            Cfe.Add(new FieldDef("C.local", "Locaux", FKind.List, Locaux[0]).Opts(Locaux));
            Cfe.Add(new FieldDef("C.vl", "Valeur locative cadastrale annuelle du local (€) – voir avis de taxe foncière / CFE", FKind.Money, "0"));
            Cfe.Add(new FieldDef("C.taux", "Taux global de CFE (commune + intercommunalité) (%)", FKind.Pct, "25"));
            Cfe.Add(new FieldDef("C.bmin", "Base minimale applicable dans ta commune (€)", FKind.Money, "500"));
            Cfe.Add(new FieldDef("C.ca2", "Chiffre d'affaires N-2 (€) – base minimale et exonération ≤ 5 000 €", FKind.Money, "0"));
            Cfe.Add(new FieldDef("C.chambre", "Frais de chambre (CCI/CMA) – % de la CFE (voir avis)", FKind.Pct, "0"));
            Cfe.Add(new FieldDef("C.gestion", "Frais de gestion – % de la CFE (voir avis)", FKind.Pct, "0"));
            Cfe.Add(new FieldDef("C.prev", "CFE payée l'année précédente (€) – pour l'acompte de juin", FKind.Money, "0"));
            Cfe.Add(new FieldDef("C.exo", "Autre exonération applicable (zone, activité…) ?", FKind.List, "Non").Opts(OuiNon));

            // ---- paramètres CFE
            decimal[][] cb = { new[] { 10000m, 250m, 597m }, new[] { 32600m, 250m, 1194m }, new[] { 100000m, 250m, 2509m }, new[] { 250000m, 250m, 4182m }, new[] { 500000m, 250m, 5970m }, new[] { 9999999999m, 250m, 7764m } };
            for (int i = 0; i < 6; i++)
            {
                CfeParams.Add(new FieldDef("CB." + i + ".max", "Tranche " + (i + 1) + " : CA N-2 jusqu'à (€)", FKind.Money, Fmt.Dec(cb[i][0])));
                CfeParams.Add(new FieldDef("CB." + i + ".min", "Tranche " + (i + 1) + " : base minimale mini (€)", FKind.Money, Fmt.Dec(cb[i][1])));
                CfeParams.Add(new FieldDef("CB." + i + ".mx", "Tranche " + (i + 1) + " : base minimale maxi (€)", FKind.Money, Fmt.Dec(cb[i][2])));
            }
            CfeParams.Add(new FieldDef("CP.exo", "Seuil d'exonération (CA N-2 ≤, sans local) (€)", FKind.Money, "5000"));
            CfeParams.Add(new FieldDef("CP.reduc", "Réduction de base l'année suivant la création (%)", FKind.Pct, "50"));
            CfeParams.Add(new FieldDef("CP.seuilAc", "Seuil de CFE déclenchant l'acompte de juin (€)", FKind.Money, "3000"));
            CfeParams.Add(new FieldDef("CP.partAc", "Part de l'acompte (CFE N-1) (%)", FKind.Pct, "50"));

            // ---- rentabilité
            Renta.Add(new FieldDef("R.fidMax", "Remise fidélité maximale (%)", FKind.Pct, "15"));
            Renta.Add(new FieldDef("R.ancMin", "Ancienneté minimale d'un client fidèle (mois)", FKind.Int, "6").Range(0, 120));
            Renta.Add(new FieldDef("R.assMin", "Assiduité minimale (présences / mois)", FKind.Dec, "4").Range(0, 100));
            Renta.Add(new FieldDef("R.margeMin", "Marge minimale conservée après remise (% du CA client)", FKind.Pct, "20"));
            Renta.Add(new FieldDef("R.reinvNew", "Part du revenu des nouveaux inscrits réinvestie (%)", FKind.Pct, "20"));
            Renta.Add(new FieldDef("R.reinvOth", "Part du revenu des autres clients réinvestie (%)", FKind.Pct, "5"));

            foreach (var l in new[] { Urssaf, UrssafParams, Cfe, CfeParams, Renta })
                foreach (FieldDef f in l) all[f.Key] = f;
        }

        public static FieldDef Def(string key) { return all[key]; }
        public static bool IsFiscalKey(string key)
        {
            return key.StartsWith("U.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("P.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("B.", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("C.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CB.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("CP.", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("R.", StringComparison.OrdinalIgnoreCase);
        }

        public static string Str(Database db, string key)
        {
            string v;
            if (db.Settings.TryGetValue(key, out v) && v.Length > 0) return v;
            return all[key].Default;
        }

        public static decimal Dec(Database db, string key)
        {
            decimal d;
            if (Fmt.TryDec(Str(db, key), out d)) return d;
            return Fmt.TryDec(all[key].Default, out d) ? d : 0m;
        }

        /// <summary>Pourcentage saisi (ex. 21,2) converti en fraction (0,212).</summary>
        public static decimal Frac(Database db, string key) { return Dec(db, key) / 100m; }

        public static int Int(Database db, string key)
        {
            int i;
            if (Fmt.TryInt(Str(db, key), out i)) return i;
            return Fmt.TryInt(all[key].Default, out i) ? i : 0;
        }

        public static DateTime? Date(Database db, string key)
        {
            DateTime d;
            if (Fmt.TryDate(Str(db, key), out d)) return d;
            return Fmt.TryDate(all[key].Default, out d) ? (DateTime?)d : null;
        }

        public static bool Yes(Database db, string key) { return Str(db, key) == "Oui"; }
    }
}
