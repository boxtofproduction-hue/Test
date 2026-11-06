using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GestionGym
{
    public class BaseInfo
    {
        public string Folder;
        public bool Archived;
        public int Year;
        public string SavedOn = "";
        public string Where { get { return Archived ? "Archive" : "Actuel"; } }
        public string Display { get { return Where + " – année " + Year + " – " + Path.GetFileName(Folder) + (SavedOn.Length > 0 ? " – enregistrée le " + SavedOn : ""); } }
    }

    public class PlanOptions
    {
        public int NewYear;
        public List<string> Sections = new List<string>();
        public bool CarryAllClients;
        public bool CarryActivePurchases = true;
        public bool ArchiveCurrent = true;
    }

    /// <summary>Gestion des dossiers de bases : recherche, création, archivage, planification de l'année suivante.</summary>
    public static class BaseManager
    {
        public static List<BaseInfo> Scan()
        {
            var l = new List<BaseInfo>();
            Scan(AppPaths.Actuel, false, l);
            Scan(AppPaths.Archive, true, l);
            return l;
        }

        static void Scan(string root, bool archived, List<BaseInfo> l)
        {
            if (!Directory.Exists(root)) return;
            foreach (string d in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith("_") || name.StartsWith(".")) continue;
                if (!Database.LooksLikeBase(d)) continue;
                int? y = Database.ReadYear(d);
                if (!y.HasValue) continue;
                string saved = "";
                foreach (string[] r in Csv.Read(Path.Combine(d, "meta.csv")))
                    if (r.Length >= 2 && r[0].Trim().Equals("Enregistre", StringComparison.OrdinalIgnoreCase)) saved = r[1].Trim();
                l.Add(new BaseInfo { Folder = d, Archived = archived, Year = y.Value, SavedOn = saved });
            }
        }

        public static string NewActiveFolder(int year)
        {
            string baseName = Path.Combine(AppPaths.Actuel, "Base_" + year);
            string f = baseName;
            int i = 2;
            while (Directory.Exists(f) && Database.LooksLikeBase(f)) { f = baseName + "_" + i; i++; }
            return f;
        }

        public static BaseInfo ActiveBaseFor(int year)
        {
            return Scan().Where(b => !b.Archived && b.Year == year).OrderByDescending(b => b.SavedOn).FirstOrDefault();
        }

        /// <summary>Copie la base (état en mémoire) dans le dossier Archive ; retourne le dossier créé.</summary>
        public static string ArchiveCopy(Database db)
        {
            string f = Path.Combine(AppPaths.Archive, "Base_" + db.Year + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            db.SaveCopy(f);
            return f;
        }

        /// <summary>Crée la base de l'année suivante à partir de la base courante (configuration et reports choisis).</summary>
        public static Database Plan(Database cur, PlanOptions o, out string report)
        {
            string folder = NewActiveFolder(o.NewYear);
            Database nd = Database.CreateBlank(o.NewYear, folder);
            var lines = new List<string>();
            string rep = ConfigImport.Run(cur, nd, o.Sections, true, true);
            if (rep.Length > 0) lines.Add(rep);

            Calc calc = cur.Calc;
            var keepClients = new HashSet<string>();
            DateTime jan1 = new DateTime(o.NewYear, 1, 1);
            int carried = 0;
            if (o.CarryActivePurchases)
            {
                foreach (Purchase p in cur.Purchases.OrderBy(x => x.Id))
                {
                    Pack k = calc.PackOf(p);
                    if (k == null) continue;
                    DateTime? end = calc.EffectiveEnd(p);
                    bool alive = !end.HasValue || end.Value >= jan1;
                    if (alive && k.IsPooled)
                    {
                        int? rest = calc.Remaining(p);
                        if (rest.HasValue && rest.Value <= 0) alive = false;
                    }
                    if (!alive) continue;
                    if (cur.FindClient(p.ClientId) == null) continue;
                    if (nd.FindPack(p.PackName) == null)
                    {
                        nd.Packs.Add(k.Clone());   // le pack est nécessaire au calcul des achats reportés
                        lines.Add("Pack « " + k.Name + " » ajouté au catalogue de la nouvelle base (nécessaire aux achats reportés).");
                    }
                    Purchase np = p.Clone();
                    np.CarriedUsed = calc.UsedCredits(p);
                    nd.Purchases.Add(np);
                    keepClients.Add(p.ClientId);
                    carried++;
                }
                lines.Add(Fmt.Plural(carried, "achat en cours reporté", "achats en cours reportés") + " (crédits déjà utilisés conservés).");
            }
            int nc = 0;
            foreach (Client c in cur.Clients)
            {
                if (o.CarryAllClients || keepClients.Contains(c.Id)) { nd.Clients.Add(c.Clone()); nc++; }
            }
            lines.Add(Fmt.Plural(nc, "client reporté", "clients reportés") + ".");
            nd.Dirty = true;
            report = string.Join("\n", lines);
            return nd;
        }
    }
}
