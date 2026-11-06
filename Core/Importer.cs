using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GestionGym
{
    public class ImportTargetDef
    {
        public string Key, Label;
        public string[] Canon;                      // en-têtes CSV canoniques (ceux des fichiers de la base)
        public string[][] Syn;                      // synonymes normalisés, un tableau par colonne canonique
        public string[] Required;                   // groupes de colonnes obligatoires ("Nom|Prenom" = l'une ou l'autre)
    }

    public class DetectedTable
    {
        public string SheetName = "";
        public ImportTargetDef Target;
        public int HeaderRow;
        public int[] Map = new int[0];              // colonne canonique -> colonne source (-1 = absente)
        public List<string[]> Rows = new List<string[]>();
        public List<int> RowNumbers = new List<int>();
        public bool Truncated;
        public bool Selected = true;

        public string MappingText(string[] header)
        {
            var l = new List<string>();
            for (int i = 0; i < Target.Canon.Length; i++)
                if (Map[i] >= 0 && Map[i] < header.Length) l.Add(Target.Canon[i] + " ← « " + header[Map[i]] + " »");
            return string.Join(", ", l);
        }
    }

    public class ImportReport
    {
        public readonly List<string> Lines = new List<string>();
        public readonly List<string> Problems = new List<string>();
        public int Added, Skipped, Rejected;
        public void Reject(string where, string why)
        {
            Rejected++;
            if (Problems.Count < 60) Problems.Add(where + " : " + why);
            else if (Problems.Count == 60) Problems.Add("… (les autres rejets ne sont pas détaillés)");
        }
    }

    public static class Importer
    {
        public static readonly ImportTargetDef[] Targets = BuildTargets();

        static string[] S(params string[] s) { return s; }

        static ImportTargetDef[] BuildTargets()
        {
            var cl = new ImportTargetDef
            {
                Key = "clients", Label = "Clients", Canon = Client.Header,
                Syn = new[] { S("id", "identifiant", "idclient", "numero", "numeroclient"), S("nom", "nomdefamille", "lastname", "name", "famille"), S("prenom", "firstname", "prenoms"), S("sexe", "genre", "civilite"), S("age", "ageans"), S("distancekm", "distance", "km", "distancedugym", "distancedomicile"), S("notes", "note", "commentaire", "commentaires", "remarque", "remarques") },
                Required = S("Nom|Prenom")
            };
            var pk = new ImportTargetDef
            {
                Key = "packs", Label = "Packs (catalogue)", Canon = Pack.Header,
                Syn = new[] { S("nom", "pack", "nomdupack", "nompack", "libelle", "designation"), S("categorie", "typedepack", "categoriepack"), S("credits", "nbcredits", "nombredecredits", "seances", "nombredeseances"), S("creditspar", "par", "periodicite", "periodecredits"), S("validitemois", "validite", "validitedumois", "dureedevalidite"), S("engagement", "typeengagement"), S("dureeengagementmois", "dureeengagement", "engagementmois", "dureedengagement"), S("echeance", "paiement", "echeancedepaiement", "echeancepaiement", "modedepaiement"), S("eligibilite", "eligible"), S("reconductiontacite", "reconduction", "renouvellement", "tacitereconduction"), S("prix", "prixeur", "tarif", "montant", "prixparecheance"), S("actif", "active", "actifoui") },
                Required = S("Nom", "Credits", "Prix")
            };
            var ac = new ImportTargetDef
            {
                Key = "achats", Label = "Achats de packs",
                Canon = Purchase.Header.Concat(new[] { "Nom", "Prenom" }).ToArray(),
                Syn = new[] { S("id", "n", "numero", "numeroachat"), S("clientid", "client", "idclient", "nomclient", "clientnom"), S("pack", "packachete", "nomdupack", "abonnement", "typedabonnement", "formule"), S("datedebut", "debut", "dateachat", "datedachat", "dateabonnement", "datedabonnement", "date"), S("datefin", "fin", "datedefin"), S("motifdefin", "motiffin", "motif"), S("creditsdejautilises", "creditsutilises", "dejautilises"), S("note", "notes", "commentaire"), S("nom", "nomdefamille", "lastname"), S("prenom", "firstname") },
                Required = S("Pack", "DateDebut", "ClientID|Nom")
            };
            var pr = new ImportTargetDef
            {
                Key = "presences", Label = "Présences",
                Canon = Attendance.Header.Concat(new[] { "Nom", "Prenom" }).ToArray(),
                Syn = new[] { S("id", "n", "numero"), S("date", "datedeseance", "dateprésence", "datepresence", "jour"), S("creneau", "horaire", "heure", "slot", "creneauhoraire"), S("activite", "cours", "seance", "discipline"), S("clientid", "client", "idclient", "nomclient"), S("achatid", "achat", "idachat", "numeroachat"), S("nom", "nomdefamille", "lastname"), S("prenom", "firstname") },
                Required = S("Date", "Creneau", "ClientID|Nom")
            };
            var ch = new ImportTargetDef
            {
                Key = "charges", Label = "Charges fixes", Canon = Charge.Header,
                Syn = new[] { S("libelle", "libelledelacharge", "charge", "designation", "nom", "intitule"), S("categorie", "typedecharge"), S("montant", "montanteur", "prix", "cout"), S("frequence", "periodicite", "rythme"), S("debut", "datedebut", "datededebut"), S("fin", "datefin", "datedefin") },
                Required = S("Libelle", "Montant")
            };
            var se = new ImportTargetDef
            {
                Key = "seances", Label = "Planning (séances)", Canon = new[] { "Date", "Creneau", "Activite" },
                Syn = new[] { S("date", "jour", "datedeseance"), S("creneau", "horaire", "heure", "slot"), S("activite", "cours", "seance", "discipline") },
                Required = S("Date", "Creneau", "Activite")
            };
            return new[] { cl, pk, ac, pr, ch, se };
        }

        public static ImportTargetDef TargetByKey(string key) { return Targets.FirstOrDefault(t => t.Key == key); }

        static string Key(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in Fmt.Norm(s ?? "")) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- détection
        static int[] MapColumns(string[] header, ImportTargetDef t)
        {
            var map = Enumerable.Repeat(-1, t.Canon.Length).ToArray();
            var keys = header.Select(Key).ToArray();
            var used = new HashSet<int>();
            // 1) correspondance exacte (nom canonique puis synonymes), 2) début de libellé
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < t.Canon.Length; i++)
                {
                    if (map[i] >= 0) continue;
                    var cands = new List<string> { Key(t.Canon[i]) };
                    cands.AddRange(t.Syn[i]);
                    for (int c = 0; c < keys.Length && map[i] < 0; c++)
                    {
                        if (used.Contains(c) || keys[c].Length == 0) continue;
                        foreach (string s in cands)
                        {
                            bool ok = pass == 0 ? keys[c] == s : (s.Length >= 4 && keys[c].StartsWith(s));
                            if (ok) { map[i] = c; used.Add(c); break; }
                        }
                    }
                }
            return map;
        }

        static bool RequiredOk(ImportTargetDef t, int[] map)
        {
            foreach (string g in t.Required)
            {
                bool any = false;
                foreach (string alt in g.Split('|'))
                {
                    int i = Array.IndexOf(t.Canon, alt);
                    if (i >= 0 && map[i] >= 0) any = true;
                }
                if (!any) return false;
            }
            return true;
        }

        public static DetectedTable Detect(SheetData sh, string forcedKey)
        {
            DetectedTable best = null;
            int bestScore = 0;
            foreach (ImportTargetDef t in Targets)
            {
                if (forcedKey != null && t.Key != forcedKey) continue;
                int limit = Math.Min(sh.Rows.Count, 20);
                for (int r = 0; r < limit; r++)
                {
                    string[] header = sh.Rows[r];
                    if (header.Count(x => !string.IsNullOrWhiteSpace(x)) < 2) continue;
                    int[] map = MapColumns(header, t);
                    if (!RequiredOk(t, map)) continue;
                    int score = map.Count(m => m >= 0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new DetectedTable { SheetName = sh.Name, Target = t, HeaderRow = r, Map = map, Truncated = sh.Truncated };
                    }
                    break;      // la première ligne reconnue sert d'en-tête
                }
            }
            if (best != null) Fill(best, sh);
            return best;
        }

        static void Fill(DetectedTable d, SheetData sh)
        {
            for (int r = d.HeaderRow + 1; r < sh.Rows.Count; r++)
            {
                string[] src = sh.Rows[r];
                var row = new string[d.Target.Canon.Length];
                bool any = false;
                for (int i = 0; i < row.Length; i++)
                {
                    int c = d.Map[i];
                    row[i] = c >= 0 && c < src.Length ? (src[c] ?? "").Trim() : "";
                    if (row[i].Length > 0) any = true;
                }
                if (!any) continue;
                string first = src.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
                if (Key(first) == "total" || Key(first) == "totaux") continue;
                d.Rows.Add(row);
                d.RowNumbers.Add(r + 1);
            }
        }

        /// <summary>Enregistre les données converties au format CSV (colonnes canoniques de la base) et retourne le chemin.</summary>
        public static string SaveCsv(DetectedTable t, string folder, string sourceName)
        {
            Directory.CreateDirectory(folder);
            string name = AppPaths.SafeName(Path.GetFileNameWithoutExtension(sourceName)) + "_" + AppPaths.SafeName(t.SheetName) + "_" + t.Target.Key + ".csv";
            string path = Path.Combine(folder, name);
            var rows = new List<string[]> { t.Target.Canon };
            rows.AddRange(t.Rows);
            Csv.Write(path, rows);
            return path;
        }

        // ---------------------------------------------------------------- application dans la base
        public class Options
        {
            public bool AutoAddLists = true;        // créer les activités et créneaux inconnus
            public bool LinkAttendances = true;     // rattacher automatiquement les présences à un pack
        }

        public static ImportReport Apply(Database db, List<DetectedTable> tables, Options opt)
        {
            var rep = new ImportReport();
            var clientMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var purchaseMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string[] order = { "clients", "packs", "achats", "presences", "charges", "seances" };
            db.Batch(() =>
            {
                foreach (string key in order)
                    foreach (DetectedTable t in tables.Where(x => x.Selected && x.Target.Key == key))
                    {
                        int a0 = rep.Added, s0 = rep.Skipped, r0 = rep.Rejected;
                        switch (key)
                        {
                            case "clients": ApplyClients(db, t, rep, clientMap); break;
                            case "packs": ApplyPacks(db, t, rep); break;
                            case "achats": ApplyPurchases(db, t, rep, clientMap, purchaseMap); break;
                            case "presences": ApplyAttendances(db, t, rep, clientMap, purchaseMap, opt); break;
                            case "charges": ApplyCharges(db, t, rep); break;
                            case "seances": ApplySessions(db, t, rep, opt); break;
                        }
                        rep.Lines.Add(t.Target.Label + " (feuille « " + t.SheetName + " ») : " + (rep.Added - a0) + " ajouté(s), " + (rep.Skipped - s0) + " déjà présent(s) ignoré(s), " + (rep.Rejected - r0) + " rejeté(s).");
                    }
            });
            return rep;
        }

        static CsvTable Table(ImportTargetDef t, string[] row)
        {
            return new CsvTable(new List<string[]> { t.Canon, row });
        }

        static string At(DetectedTable t, int i) { return t.SheetName + ", ligne " + t.RowNumbers[i]; }

        static int Col(ImportTargetDef t, string name) { return Array.IndexOf(t.Canon, name); }

        // ---- clients
        static void ApplyClients(Database db, DetectedTable t, ImportReport rep, Dictionary<string, string> clientMap)
        {
            int sx = Col(t.Target, "Sexe"), ag = Col(t.Target, "Age");
            for (int i = 0; i < t.Rows.Count; i++)
            {
                string[] row = (string[])t.Rows[i].Clone();
                string srcId = row[0];
                string n = Fmt.Norm(row[sx]);
                row[sx] = n.StartsWith("f") ? "Femme" : (n.StartsWith("h") || n.StartsWith("m") ? "Homme" : "Autre");
                decimal ageD;
                if (row[ag].Length > 0 && Fmt.TryDec(row[ag], out ageD)) row[ag] = Math.Round(ageD).ToString("0");
                Client c;
                string err = Client.FromCsv(Table(t.Target, row), row, out c);
                if (err != null) { rep.Reject(At(t, i), err); continue; }
                Client dup = db.FindDuplicate(c.LastName, c.FirstName, null);
                if (dup != null)
                {
                    if (srcId.Length > 0) clientMap[srcId] = dup.Id;
                    rep.Skipped++;
                    continue;
                }
                try
                {
                    db.ImportClient(c);
                    if (srcId.Length > 0) clientMap[srcId] = c.Id;
                    rep.Added++;
                }
                catch (InvalidOperationException ex) { rep.Reject(At(t, i), ex.Message); }
            }
        }

        // ---- packs
        static void ApplyPacks(Database db, DetectedTable t, ImportReport rep)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                Pack p;
                string err = Pack.FromCsv(Table(t.Target, t.Rows[i]), t.Rows[i], out p);
                if (err != null) { rep.Reject(At(t, i), err); continue; }
                if (db.FindPack(p.Name) != null) { rep.Skipped++; continue; }
                try { db.AddPack(p); rep.Added++; }
                catch (InvalidOperationException ex) { rep.Reject(At(t, i), ex.Message); }
            }
        }

        // ---- résolution d'un client (identifiant source, identifiant de la base, « C001 – Nom Prénom » ou nom/prénom)
        static Client ResolveClient(Database db, Dictionary<string, string> clientMap, string idCell, string last, string first)
        {
            string v = (idCell ?? "").Trim();
            string mapped;
            if (v.Length > 0)
            {
                if (clientMap.TryGetValue(v, out mapped)) { Client cm = db.FindClient(mapped); if (cm != null) return cm; }
                Client byId = db.FindClient(v);
                if (byId != null) return byId;
                int sep = v.IndexOfAny(new[] { '–', '-' });
                if (sep > 0)
                {
                    string left = v.Substring(0, sep).Trim();
                    if (clientMap.TryGetValue(left, out mapped)) { Client cm = db.FindClient(mapped); if (cm != null) return cm; }
                    Client byLeft = db.FindClient(left);
                    if (byLeft != null) return byLeft;
                    v = v.Substring(sep + 1).Trim();
                }
            }
            string full = Fmt.Norm((v.Length > 0 ? v : ((last ?? "") + " " + (first ?? ""))).Trim());
            full = string.Join(" ", full.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (full.Length == 0) return null;
            foreach (Client c in db.Clients)
            {
                string a = Fmt.Norm((c.LastName + " " + c.FirstName).Trim());
                string b = Fmt.Norm((c.FirstName + " " + c.LastName).Trim());
                if (a == full || b == full) return c;
            }
            // nom seul, s'il est unique
            var same = db.Clients.Where(c => Fmt.Norm(c.LastName.Trim()) == full).ToList();
            return same.Count == 1 ? same[0] : null;
        }

        // ---- achats
        static void ApplyPurchases(Database db, DetectedTable t, ImportReport rep, Dictionary<string, string> clientMap, Dictionary<string, int> purchaseMap)
        {
            int iId = Col(t.Target, "ID"), iCl = Col(t.Target, "ClientID"), iPk = Col(t.Target, "Pack"), iN = Col(t.Target, "Nom"), iP = Col(t.Target, "Prenom");
            for (int i = 0; i < t.Rows.Count; i++)
            {
                string[] row = (string[])t.Rows[i].Clone();
                string srcId = row[iId];
                Client c = ResolveClient(db, clientMap, row[iCl], row[iN], row[iP]);
                if (c == null) { rep.Reject(At(t, i), "client introuvable (« " + (row[iCl].Length > 0 ? row[iCl] : row[iN] + " " + row[iP]).Trim() + " ») : importe d'abord les clients"); continue; }
                Pack pk = db.FindPack(row[iPk]);
                if (pk == null)
                {
                    string kn = Fmt.Norm(row[iPk]);
                    pk = db.Packs.FirstOrDefault(p => Fmt.Norm(p.Name) == kn) ?? db.Packs.FirstOrDefault(p => kn.Length >= 4 && Fmt.Norm(p.Name).StartsWith(kn));
                }
                if (pk == null) { rep.Reject(At(t, i), "pack inconnu (« " + row[iPk] + " ») : ajoute-le d'abord dans le catalogue des packs"); continue; }
                row[iCl] = c.Id;
                row[iPk] = pk.Name;
                row[iId] = "1";
                Purchase p0;
                string err = Purchase.FromCsv(Table(t.Target, row), row, out p0);
                if (err != null) { rep.Reject(At(t, i), err); continue; }
                try
                {
                    Purchase added = db.AddPurchase(p0);
                    if (srcId.Length > 0) purchaseMap[srcId] = added.Id;
                    rep.Added++;
                }
                catch (InvalidOperationException ex) { rep.Reject(At(t, i), ex.Message); }
            }
        }

        // ---- listes (activités / créneaux)
        static string MatchList(List<string> list, string value, int maxLen, Action<string> add, bool autoAdd, out string error)
        {
            error = null;
            string v = Fmt.Clean(value);
            if (v.Length == 0) { error = "valeur vide"; return null; }
            string k = Key(v);
            foreach (string s in list) if (Key(s) == k) return s;
            if (!autoAdd) { error = "« " + v + " » n'existe pas dans la configuration"; return null; }
            try { add(v); return v; }
            catch (InvalidOperationException ex) { error = ex.Message; return null; }
        }

        static string FindActivity(Database db, string v, bool auto, out string err) { return MatchList(db.Activities, v, 40, db.AddActivity, auto, out err); }
        static string FindSlot(Database db, string v, bool auto, out string err) { return MatchList(db.Slots, v, 20, db.AddSlot, auto, out err); }

        // ---- présences
        static void ApplyAttendances(Database db, DetectedTable t, ImportReport rep, Dictionary<string, string> clientMap, Dictionary<string, int> purchaseMap, Options opt)
        {
            int iD = Col(t.Target, "Date"), iS = Col(t.Target, "Creneau"), iA = Col(t.Target, "Activite"), iC = Col(t.Target, "ClientID"), iP = Col(t.Target, "AchatID"), iN = Col(t.Target, "Nom"), iF = Col(t.Target, "Prenom");
            for (int i = 0; i < t.Rows.Count; i++)
            {
                string[] row = t.Rows[i];
                DateTime d;
                if (!Fmt.TryDate(row[iD], out d)) { rep.Reject(At(t, i), "date invalide (« " + row[iD] + " »)"); continue; }
                if (d.Year != db.Year) { rep.Reject(At(t, i), "date hors de l'année " + db.Year + " (" + Fmt.Date(d) + ")"); continue; }
                Client c = ResolveClient(db, clientMap, row[iC], row[iN], row[iF]);
                if (c == null) { rep.Reject(At(t, i), "client introuvable (« " + (row[iC].Length > 0 ? row[iC] : row[iN] + " " + row[iF]).Trim() + " »)"); continue; }
                string err;
                string slot = FindSlot(db, row[iS], opt.AutoAddLists, out err);
                if (slot == null) { rep.Reject(At(t, i), "créneau : " + err); continue; }
                string act = row[iA].Length == 0 ? db.SessionActivity(d, slot) : FindActivity(db, row[iA], opt.AutoAddLists, out err);
                if (string.IsNullOrEmpty(act)) { rep.Reject(At(t, i), "activité : " + (err ?? "non précisée et aucune séance programmée à cette date")); continue; }
                var a = new Attendance { Date = d, Slot = slot, Activity = act, ClientId = c.Id };
                int pid;
                if (row[iP].Length > 0 && purchaseMap.TryGetValue(row[iP], out pid)) a.PurchaseId = pid;
                else if (opt.LinkAttendances)
                {
                    foreach (Purchase p in db.Purchases.Where(x => x.ClientId == c.Id).OrderByDescending(x => x.Start))
                    {
                        if (p.Start > d) continue;
                        a.PurchaseId = p.Id;
                        List<Issue> pi = db.Calc.Checker.PackCheck(a);
                        if (!pi.Any(x => x.Severity == Sev.Error)) break;
                        a.PurchaseId = 0;
                    }
                }
                try { db.AddAttendance(a); rep.Added++; }
                catch (InvalidOperationException ex)
                {
                    if (ex.Message.Contains("déjà marqué")) rep.Skipped++;
                    else rep.Reject(At(t, i), ex.Message);
                }
            }
        }

        // ---- charges
        static void ApplyCharges(Database db, DetectedTable t, ImportReport rep)
        {
            int iDb = Col(t.Target, "Debut"), iF = Col(t.Target, "Frequence");
            for (int i = 0; i < t.Rows.Count; i++)
            {
                string[] row = (string[])t.Rows[i].Clone();
                if (row[iDb].Length == 0) row[iDb] = "01/01/" + db.Year;
                if (row[iF].Length == 0) row[iF] = "Mensuel";
                Charge c;
                string err = Charge.FromCsv(Table(t.Target, row), row, out c);
                if (err != null) { rep.Reject(At(t, i), err); continue; }
                if (db.Charges.Any(x => x.Label.Equals(c.Label, StringComparison.OrdinalIgnoreCase) && x.Amount == c.Amount && x.Frequency == c.Frequency)) { rep.Skipped++; continue; }
                try { db.AddCharge(c); rep.Added++; }
                catch (InvalidOperationException ex) { rep.Reject(At(t, i), ex.Message); }
            }
        }

        // ---- séances
        static void ApplySessions(Database db, DetectedTable t, ImportReport rep, Options opt)
        {
            for (int i = 0; i < t.Rows.Count; i++)
            {
                string[] row = t.Rows[i];
                DateTime d;
                if (!Fmt.TryDate(row[0], out d)) { rep.Reject(At(t, i), "date invalide (« " + row[0] + " »)"); continue; }
                string err;
                string slot = FindSlot(db, row[1], opt.AutoAddLists, out err);
                if (slot == null) { rep.Reject(At(t, i), "créneau : " + err); continue; }
                string act = FindActivity(db, row[2], opt.AutoAddLists, out err);
                if (act == null) { rep.Reject(At(t, i), "activité : " + err); continue; }
                try
                {
                    if (db.Sessions.Any(s => s.Date == d.Date && s.Slot == slot && s.Activity == act)) { rep.Skipped++; continue; }
                    db.SetSession(d, slot, act);
                    rep.Added++;
                }
                catch (InvalidOperationException ex) { rep.Reject(At(t, i), ex.Message); }
            }
        }
    }
}
