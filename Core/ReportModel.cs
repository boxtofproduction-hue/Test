using System;
using System.Collections.Generic;
using System.Linq;

namespace GestionGym
{
    public class Series
    {
        public string Name = "";
        public double[] Values = new double[0];
        public string Color = "2F5597";
    }

    public enum BlockKind { Table, Pairs, Chart, Note }

    /// <summary>Un bloc d'une feuille de rapport : tableau, liste libellé/valeur, graphique ou note. Sert aux écrans ET aux exports (Excel, OpenOffice, PDF).</summary>
    public class Block
    {
        public BlockKind Kind;
        public string Title = "";
        public string[] Headers = new string[0];
        public string[] Formats = new string[0];     // par colonne : null, N0, N2, dd/MM/yyyy, 0.0 %
        public double[] Widths = new double[0];
        public List<object[]> Rows = new List<object[]>();
        public List<KV> Pairs = new List<KV>();
        public string[] Labels = new string[0];
        public List<Series> Series = new List<Series>();
        public string Text = "";
        public bool Horizontal;                      // graphique en barres horizontales (libellés longs)
    }

    public class Sheet
    {
        public string Name = "";
        public string Title = "";
        public List<Block> Blocks = new List<Block>();
    }

    public static class ReportBuilder
    {
        const string C1 = "2F5597", C2 = "ED7D31", C3 = "548235", C4 = "C00000", C5 = "7F7F7F";

        static string[] Months() { return Enumerable.Range(1, 12).Select(m => Fmt.MonthName(m)).ToArray(); }
        static double[] D(IEnumerable<decimal> v) { return v.Select(x => (double)x).ToArray(); }
        static object Z(decimal v) { return v == 0 ? null : (object)v; }

        static Block Table(string title, string[] headers, string[] formats, double[] widths, List<object[]> rows)
        {
            return new Block { Kind = BlockKind.Table, Title = title, Headers = headers, Formats = formats, Widths = widths, Rows = rows };
        }

        static Block Chart(string title, string[] labels, bool horizontal, params Series[] s)
        {
            var b = new Block { Kind = BlockKind.Chart, Title = title, Labels = labels, Horizontal = horizontal };
            b.Series.AddRange(s);
            return b;
        }

        static Block Pairs(string title, List<KV> p) { return new Block { Kind = BlockKind.Pairs, Title = title, Pairs = p }; }
        static Block Note(string t) { return new Block { Kind = BlockKind.Note, Text = t }; }

        // ------------------------------------------------------------------ tableaux partagés par les écrans et les exports
        public static Block ChargesTable(Database db)
        {
            var rows = new List<object[]>();
            var f = new List<string> { null, null, "N2", null, "dd/MM/yyyy", "dd/MM/yyyy" };
            var h = new List<string> { "Libellé", "Catégorie", "Montant (€)", "Fréquence", "Début", "Fin" };
            var w = new List<double> { 210, 190, 90, 90, 90, 90 };
            for (int m = 1; m <= 12; m++) { h.Add(Fmt.MonthName(m)); f.Add("N2"); w.Add(78); }
            h.Add("Total année (€)"); f.Add("N2"); w.Add(100);
            foreach (Charge c in db.Charges)
            {
                var r = new List<object> { c.Label, c.Category, c.Amount, c.Frequency, c.Start, c.End.HasValue ? (object)c.End.Value : null };
                for (int m = 1; m <= 12; m++) r.Add(Z(c.AmountFor(db.Year, m)));
                r.Add(c.YearTotal(db.Year));
                rows.Add(r.ToArray());
            }
            if (db.Charges.Count > 0)
            {
                var tot = new List<object> { "TOTAL", "", null, "", null, null };
                decimal[] bm = Finance.ChargesByMonth(db);
                for (int m = 0; m < 12; m++) tot.Add(bm[m]);
                tot.Add(bm.Sum());
                rows.Add(tot.ToArray());
            }
            return Table("Charges fixes", h.ToArray(), f.ToArray(), w.ToArray(), rows);
        }

        public static Block TrackingTable(Database db)
        {
            Calc calc = db.Calc;
            var h = new List<string> { "Client", "Pack", "Type", "Début", "Fin / expiration", "Statut", "Crédits utilisés", "Crédits restants", "Revenu année (€)" };
            var f = new List<string> { null, null, null, "dd/MM/yyyy", "dd/MM/yyyy", null, "N0", "N0", "N2" };
            var w = new List<double> { 200, 200, 110, 90, 100, 80, 80, 80, 100 };
            for (int m = 1; m <= 12; m++) { h.Add(Fmt.MonthName(m)); f.Add("N2"); w.Add(78); }
            var rows = new List<object[]>();
            foreach (Purchase p in db.Purchases.OrderBy(x => x.Start).ThenBy(x => x.Id))
            {
                Client c = db.FindClient(p.ClientId);
                DateTime? end = calc.EffectiveEnd(p);
                int? rest = calc.Remaining(p);
                decimal[] mr = calc.MonthlyRevenue(p);
                var r = new List<object> { c != null ? c.Label : p.ClientId, p.PackName, calc.Kind(p), p.Start, end.HasValue ? (object)end.Value : null, calc.Status(p), calc.UsedCredits(p), rest.HasValue ? (object)rest.Value : null, mr.Sum() };
                foreach (decimal x in mr) r.Add(Z(x));
                rows.Add(r.ToArray());
            }
            if (rows.Count > 0)
            {
                var tot = new List<object> { "TOTAL", "", "", null, null, "", null, null, null };
                decimal[] sum = calc.RevenueByMonth();
                tot[8] = sum.Sum();
                foreach (decimal x in sum) tot.Add(x);
                rows.Add(tot.ToArray());
            }
            return Table("Tracking des abonnements et packs (revenu encaissé par mois)", h.ToArray(), f.ToArray(), w.ToArray(), rows);
        }

        public static Block MonthlyTable(Database db, UrssafResult u)
        {
            var rows = new List<object[]>();
            int totSes = 0, totPres = 0;
            for (int m = 1; m <= 12; m++)
            {
                int ses = db.Sessions.Count(s => s.Date.Year == db.Year && s.Date.Month == m);
                int pres = db.Attendances.Count(a => a.Date.Year == db.Year && a.Date.Month == m);
                totSes += ses; totPres += pres;
                MonthRow r = u.Months[m - 1];
                rows.Add(new object[] { Fmt.MonthName(m), ses, pres, ses == 0 ? null : (object)(pres * 100.0 / (ses * (double)db.Capacity)), r.Ca, r.Charges, r.Total, r.ResAv });
            }
            rows.Add(new object[] { "TOTAL", totSes, totPres, totSes == 0 ? null : (object)(totPres * 100.0 / (totSes * (double)db.Capacity)), u.Total.Ca, u.Total.Charges, u.Total.Total, u.Total.ResAv });
            return Table("Synthèse mensuelle", new[] { "Mois", "Séances", "Présences", "Remplissage (%)", "CA (€)", "Charges fixes (€)", "Cotisations (€)", "Résultat avant impôt (€)" },
                new[] { null, "N0", "N0", "0.0", "N2", "N2", "N2", "N2" }, new double[] { 110, 80, 90, 110, 110, 120, 120, 150 }, rows);
        }

        public static Block UrssafMonthlyTable(UrssafResult u)
        {
            var rows = new List<object[]>();
            for (int m = 1; m <= 12; m++)
            {
                MonthRow r = u.Months[m - 1];
                rows.Add(new object[] { Fmt.MonthName(m), r.Ca, r.Cot, r.Acre, r.Cfp, r.Total, r.Vl, r.Charges, r.ResAv });
            }
            MonthRow t = u.Total;
            rows.Add(new object[] { "TOTAL", t.Ca, t.Cot, t.Acre, t.Cfp, t.Total, t.Vl, t.Charges, t.ResAv });
            return Table("Détail mensuel URSSAF", new[] { "Mois", "CA (€)", "Cotisations de base", "Réduction ACRE", "CFP", "Total cotisations", "Versement libératoire", "Charges fixes", "Résultat avant impôt" },
                new[] { null, "N2", "N2", "N2", "N2", "N2", "N2", "N2", "N2" }, new double[] { 110, 100, 120, 110, 80, 120, 130, 110, 140 }, rows);
        }

        public static Block RentaClientsTable(RentaResult R)
        {
            var rows = new List<object[]>();
            foreach (ClientProfit c in R.Clients)
                rows.Add(new object[] { c.Client.Label, c.Client.Sex, c.Client.Age > 0 ? (object)c.Client.Age : null, c.AgeBand, c.Client.DistanceKm > 0 ? (object)c.Client.DistanceKm : null, c.DistBand, c.Pack, c.Status, c.Slot, c.Activity,
                    c.Revenue, c.Presences, c.FixedShare, c.Contrib, c.Margin, c.Revenue == 0 ? null : (object)(double)(c.MarginPct * 100), c.Presences == 0 ? null : (object)c.RevPerPres, c.Rank, c.AncMonths, c.PerMonth,
                    c.IsNew ? "Oui" : "Non", c.Loyal ? "Oui" : "Non", c.DiscountPct * 100m, c.DiscountEur });
            return Table("Rentabilité par client", new[] { "Client", "Sexe", "Âge", "Tranche d'âge", "Distance (km)", "Tranche de distance", "Pack", "Statut", "Créneau préféré", "Activité préférée", "Revenu (€)", "Présences", "Part charges fixes (€)", "Cotisations (€)", "Marge nette (€)", "Marge (%)", "Revenu / présence (€)", "Rang", "Ancienneté (mois)", "Présences / mois", "Nouveau (année)", "Fidèle éligible", "Remise proposée (%)", "Remise (€/an)" },
                new[] { null, null, "N0", null, "N1", null, null, null, null, null, "N2", "N0", "N2", "N2", "N2", "0.0", "N2", "N0", "N0", "0.0", null, null, "0.0", "N2" },
                new double[] { 200, 70, 50, 110, 80, 110, 190, 80, 110, 120, 90, 80, 100, 100, 100, 70, 100, 50, 90, 90, 80, 80, 90, 90 }, rows);
        }

        public static Block SegmentTable(string title, List<Segment> segs)
        {
            var rows = new List<object[]>();
            foreach (Segment s in segs) rows.Add(new object[] { s.Name, s.Clients, s.Revenue, s.Margin, s.Presences, s.AvgRevenue, s.AvgMargin });
            return Table("Par " + title.ToLowerInvariant(), new[] { "Segment", "Clients", "Revenu (€)", "Marge nette (€)", "Présences", "Revenu moyen (€)", "Marge moyenne (€)" },
                new[] { null, "N0", "N2", "N2", "N0", "N2", "N2" }, new double[] { 200, 70, 100, 110, 80, 110, 120 }, rows);
        }

        // ------------------------------------------------------------------ feuilles complètes (exports)
        public static List<Sheet> Build(Database db)
        {
            var list = new List<Sheet>();
            Calc calc = db.Calc;
            UrssafResult u = Finance.Urssaf(db);
            CfeResult cfe = Finance.Cfe(db, u);
            RentaResult ren = Finance.Renta(db, u);
            string[] mois = Months();
            string yr = " – " + db.Year;

            // Reporting
            var rep = new Sheet { Name = "Reporting", Title = "Reporting" + yr };
            var kp = new List<KV>
            {
                new KV("CA retenu (€)", Fmt.Money(u.Ca), 2), new KV("Charges fixes (€)", Fmt.Money(u.Total.Charges)), new KV("Cotisations URSSAF (€)", Fmt.Money(u.Total.Total)),
                new KV("Impôt estimé (€)", Fmt.Money(u.Impot)), new KV("RÉSULTAT NET (€)", Fmt.Money(u.Net), 2), new KV("CFE de l'année (€)", Fmt.Money(cfe.Total)),
                new KV("Seuil de rentabilité (CA mensuel) (€)", Fmt.Money(u.Seuil)), new KV("Taux de remplissage", Fmt.Pct(calc.FillRate())),
                new KV("Remise fidélité conseillée", Fmt.Dec(ren.Advised * 100) + " %")
            };
            rep.Blocks.Add(Pairs("Indicateurs clés", kp));
            rep.Blocks.Add(MonthlyTable(db, u));
            rep.Blocks.Add(Chart("CA, cotisations et charges par mois (€)", mois, false,
                new Series { Name = "CA", Color = C1, Values = D(u.Months.Select(x => x.Ca)) },
                new Series { Name = "Cotisations", Color = C2, Values = D(u.Months.Select(x => x.Total)) },
                new Series { Name = "Charges fixes", Color = C4, Values = D(u.Months.Select(x => x.Charges)) }));
            rep.Blocks.Add(Chart("Résultat avant impôt par mois (€)", mois, false, new Series { Name = "Résultat", Color = C3, Values = D(u.Months.Select(x => x.ResAv)) }));
            rep.Blocks.Add(Chart("Présences par mois", mois, false, new Series { Name = "Présences", Color = C2, Values = calc.PresencesByMonth().Select(x => (double)x).ToArray() }));
            var pa = calc.PresencesByActivity();
            rep.Blocks.Add(Chart("Présences par activité", pa.Keys.ToArray(), true, new Series { Name = "Présences", Color = C3, Values = pa.Values.Select(x => (double)x).ToArray() }));
            var ps = calc.PresencesBySlot();
            rep.Blocks.Add(Chart("Présences par créneau", ps.Keys.ToArray(), true, new Series { Name = "Présences", Color = C2, Values = ps.Values.Select(x => (double)x).ToArray() }));
            var sp = calc.SalesByPack();
            rep.Blocks.Add(Chart("Ventes de packs (année)", sp.Keys.ToArray(), true, new Series { Name = "Ventes", Color = C1, Values = sp.Values.Select(x => (double)x).ToArray() }));
            var cc = Finance.ChargesByCategory(db);
            rep.Blocks.Add(Chart("Charges fixes par catégorie (€)", cc.Keys.ToArray(), true, new Series { Name = "Charges", Color = C4, Values = cc.Values.Select(x => (double)x).ToArray() }));
            rep.Blocks.Add(Note("Estimations URSSAF / impôt / CFE indicatives : à valider avec un expert-comptable."));
            list.Add(rep);

            // Clients
            var cl = new Sheet { Name = "Clients", Title = "Clients" + yr };
            var crow = new List<object[]>();
            foreach (Client c in db.Clients)
            {
                Purchase main; string st = calc.ClientStatus(c, out main);
                int pres; calc.PresencesByClient.TryGetValue(c.Id, out pres);
                crow.Add(new object[] { c.Id, c.LastName, c.FirstName, c.Sex, c.Age > 0 ? (object)c.Age : null, c.DistanceKm, main != null ? main.PackName : "", st, main != null ? (object)main.Start : null, pres, calc.FavoriteActivity(c.Id), calc.FavoriteSlot(c.Id), c.Notes });
            }
            cl.Blocks.Add(Table("Clients", new[] { "ID", "Nom", "Prénom", "Sexe", "Âge", "Distance (km)", "Pack (actif ou dernier)", "Statut", "Début du pack", "Présences (année)", "Activité préférée", "Créneau préféré", "Notes" },
                new[] { null, null, null, null, "N0", "N1", null, null, "dd/MM/yyyy", "N0", null, null, null }, new double[] { 50, 120, 110, 60, 40, 80, 190, 80, 90, 80, 120, 100, 150 }, crow));
            list.Add(cl);

            // Packs
            var pk = new Sheet { Name = "Packs", Title = "Catalogue des packs" + yr };
            var prow = new List<object[]>();
            foreach (Pack p in db.Packs)
                prow.Add(new object[] { p.Name, p.Category, p.Credits, p.Per, p.ValidityMonths, p.Engagement, p.EngagementMonths, p.Payment, p.Eligibility, p.Renewal ? "Oui" : "Non", p.Price, p.Active ? "Oui" : "Non" });
            pk.Blocks.Add(Table("Packs", new[] { "Pack", "Catégorie", "Crédits", "Crédits par", "Validité (mois)", "Engagement", "Durée engagement (mois)", "Paiement", "Éligibilité", "Reconduction", "Prix par échéance (€)", "Actif" },
                new[] { null, null, "N0", null, "N0", null, "N0", null, null, null, "N2", null }, new double[] { 210, 80, 60, 90, 80, 90, 100, 80, 130, 90, 100, 50 }, prow));
            list.Add(pk);

            // Achats
            var ac = new Sheet { Name = "Achats", Title = "Achats de packs" + yr };
            var arow = new List<object[]>();
            foreach (Purchase p in db.Purchases.OrderBy(x => x.Id))
            {
                Client c = db.FindClient(p.ClientId);
                DateTime? end = calc.EffectiveEnd(p); int? rest = calc.Remaining(p);
                arow.Add(new object[] { p.Id, c != null ? c.Label : p.ClientId, p.PackName, p.Start, end.HasValue ? (object)end.Value : null, calc.Status(p), calc.UsedCredits(p), rest.HasValue ? (object)rest.Value : null, calc.YearRevenue(p), Calc.IssueText(calc.PurchaseIssues(p)), p.Note });
            }
            ac.Blocks.Add(Table("Achats", new[] { "N°", "Client", "Pack", "Début", "Fin / expiration", "Statut", "Utilisés", "Restants", "Revenu année (€)", "Contrôle", "Note" },
                new[] { "N0", null, null, "dd/MM/yyyy", "dd/MM/yyyy", null, "N0", "N0", "N2", null, null }, new double[] { 40, 200, 200, 90, 100, 80, 70, 70, 100, 200, 150 }, arow));
            list.Add(ac);

            // Présences
            var pr = new Sheet { Name = "Présences", Title = "Présences" + yr };
            var rrow = new List<object[]>();
            foreach (Attendance a in db.Attendances.OrderBy(x => x.Date).ThenBy(x => x.Id))
            {
                Client c = db.FindClient(a.ClientId);
                rrow.Add(new object[] { a.Id, a.Date, a.Slot, a.Activity, c != null ? c.Label : a.ClientId, a.PurchaseId });
            }
            pr.Blocks.Add(Table("Présences", new[] { "N°", "Date", "Créneau", "Activité", "Client", "Achat n°" }, new[] { "N0", "dd/MM/yyyy", null, null, null, "N0" }, new double[] { 50, 90, 110, 130, 220, 70 }, rrow));
            list.Add(pr);

            // Planning
            var pl = new Sheet { Name = "Planning", Title = "Planning des séances" + yr };
            var lrow = new List<object[]>();
            foreach (Session s in db.Sessions.OrderBy(x => x.Date).ThenBy(x => db.Slots.IndexOf(x.Slot)))
            {
                int n = db.Attendances.Count(a => a.Date == s.Date && a.Slot == s.Slot);
                lrow.Add(new object[] { s.Date, s.Date.ToString("dddd", Fmt.Fr), db.DayNature(s.Date), s.Slot, s.Activity, n, db.Capacity });
            }
            pl.Blocks.Add(Table("Séances programmées", new[] { "Date", "Jour", "Nature du jour", "Créneau", "Activité", "Présents", "Capacité" }, new[] { "dd/MM/yyyy", null, null, null, null, "N0", "N0" }, new double[] { 90, 90, 100, 110, 140, 70, 70 }, lrow));
            list.Add(pl);

            // Charges
            var ch = new Sheet { Name = "Charges fixes", Title = "Charges fixes" + yr };
            ch.Blocks.Add(ChargesTable(db));
            ch.Blocks.Add(Chart("Charges fixes par mois (€)", mois, false, new Series { Name = "Charges", Color = C4, Values = D(Finance.ChargesByMonth(db)) }));
            ch.Blocks.Add(Chart("Charges par catégorie (€)", cc.Keys.ToArray(), true, new Series { Name = "Charges", Color = C4, Values = cc.Values.Select(x => (double)x).ToArray() }));
            list.Add(ch);

            // Tracking
            var tr = new Sheet { Name = "Tracking", Title = "Tracking des abonnements" + yr };
            tr.Blocks.Add(TrackingTable(db));
            tr.Blocks.Add(Chart("Revenu encaissé par mois (€)", mois, false, new Series { Name = "Revenu", Color = C1, Values = D(calc.RevenueByMonth()) }));
            list.Add(tr);

            // URSSAF
            var us = new Sheet { Name = "URSSAF", Title = "Calcul URSSAF et résultat net" + yr };
            us.Blocks.Add(Pairs("Profil", Fld.Urssaf.Select(f => new KV(f.Label, Fld.Str(db, f.Key))).ToList()));
            us.Blocks.Add(Pairs("Résultats annuels", u.Lines()));
            us.Blocks.Add(UrssafMonthlyTable(u));
            us.Blocks.Add(Chart("Cotisations et résultat par mois (€)", mois, false,
                new Series { Name = "Cotisations", Color = C2, Values = D(u.Months.Select(x => x.Total)) },
                new Series { Name = "Résultat avant impôt", Color = C3, Values = D(u.Months.Select(x => x.ResAv)) }));
            us.Blocks.Add(Chart("Où va le CA ? (€)", new[] { "Cotisations", "Charges fixes", "Impôt", "Résultat net" }, true,
                new Series { Name = "Montant", Color = C1, Values = new[] { (double)u.Total.Total, (double)u.Total.Charges, (double)u.Impot, (double)u.Net } }));
            us.Blocks.Add(Note("Taux 2026 modifiables dans l'application (Taux et barèmes). Estimation indicative, à valider avec un expert-comptable."));
            list.Add(us);

            // CFE
            var cf = new Sheet { Name = "CFE", Title = "CFE – Cotisation foncière des entreprises" + yr };
            cf.Blocks.Add(Pairs("Profil CFE", Fld.Cfe.Select(f => new KV(f.Label, Fld.Str(db, f.Key))).ToList()));
            cf.Blocks.Add(Pairs("Calcul", cfe.Lines()));
            cf.Blocks.Add(Chart("CFE à payer par mois (€)", mois, false, new Series { Name = "CFE", Color = C2, Values = D(cfe.Months) }));
            list.Add(cf);

            // Rentabilité
            var rt = new Sheet { Name = "Rentabilité", Title = "Rentabilité par client" + yr };
            rt.Blocks.Add(Pairs("Synthèse et remise fidélité", ren.Lines()));
            rt.Blocks.Add(RentaClientsTable(ren));
            foreach (var kv in ren.Segments)
            {
                rt.Blocks.Add(SegmentTable(kv.Key, kv.Value));
                rt.Blocks.Add(Chart("Revenu et marge – " + kv.Key.ToLowerInvariant(), kv.Value.Select(s => s.Name).ToArray(), false,
                    new Series { Name = "Revenu", Color = C1, Values = kv.Value.Select(s => (double)s.Revenue).ToArray() },
                    new Series { Name = "Marge nette", Color = C3, Values = kv.Value.Select(s => (double)s.Margin).ToArray() }));
            }
            list.Add(rt);

            // Configuration
            var cg = new Sheet { Name = "Configuration", Title = "Configuration" + yr };
            var grow = new List<object[]>();
            int n2 = Math.Max(db.Activities.Count, db.Slots.Count);
            for (int i = 0; i < n2; i++) grow.Add(new object[] { i < db.Activities.Count ? db.Activities[i] : "", i < db.Slots.Count ? db.Slots[i] : "" });
            cg.Blocks.Add(Table("Activités et créneaux", new[] { "Activité", "Créneau" }, new string[] { null, null }, new double[] { 200, 200 }, grow));
            var par = new List<KV>();
            par.Add(new KV("Année", db.Year.ToString()));
            par.Add(new KV("Capacité par séance", db.Capacity.ToString()));
            par.Add(new KV("Alerte d'expiration (jours)", db.AlertDays.ToString()));
            foreach (var kv in db.Settings.Where(k => !Fld.IsFiscalKey(k.Key)).OrderBy(k => k.Key)) par.Add(new KV(kv.Key, kv.Value));
            cg.Blocks.Add(Pairs("Paramètres généraux", par));
            list.Add(cg);
            return list;
        }
    }
}
