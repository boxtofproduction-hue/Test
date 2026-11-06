using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GestionGym
{
    /// <summary>Aide d'utilisation intégrée : liste de rubriques à gauche, texte à droite, recherche simple.</summary>
    public class HelpWindow : Window
    {
        class Topic { public string Title, Text; public override string ToString() { return Title; } }

        static HelpWindow instance;
        readonly ListBox list = new ListBox { BorderThickness = new Thickness(0) };
        readonly RichTextBox view = new RichTextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(16), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14 };
        readonly TextBox search = new TextBox { Margin = new Thickness(6), Padding = new Thickness(3) };
        readonly List<Topic> all = new List<Topic>();

        public static void ShowTopic(string title)
        {
            if (instance == null || !instance.IsLoaded)
            {
                instance = new HelpWindow();
                instance.Owner = Ui.MainWin;
                instance.Show();
            }
            else { instance.Activate(); if (instance.WindowState == WindowState.Minimized) instance.WindowState = WindowState.Normal; }
            instance.Select(title);
        }

        HelpWindow()
        {
            Title = "Aide d'utilisation – Gestion GYM";
            Width = 1000; Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            all.AddRange(Topics());
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(240, 242, 247)) };
            var hint = new TextBlock { Text = "Rechercher un mot :", Margin = new Thickness(8, 8, 8, 0), Foreground = Ui.Grey };
            DockPanel.SetDock(hint, Dock.Top); DockPanel.SetDock(search, Dock.Top);
            left.Children.Add(hint); left.Children.Add(search);
            list.Background = Brushes.Transparent;
            left.Children.Add(list);
            Grid.SetColumn(left, 0); Grid.SetColumn(view, 1);
            grid.Children.Add(left); grid.Children.Add(view);
            Content = grid;
            search.TextChanged += (s, e) => Fill();
            list.SelectionChanged += (s, e) => { Topic t = list.SelectedItem as Topic; if (t != null) Render(t); };
            KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
            Fill();
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        void Fill()
        {
            string q = (search.Text ?? "").Trim().ToLowerInvariant();
            list.Items.Clear();
            foreach (Topic t in all)
                if (q.Length == 0 || t.Title.ToLowerInvariant().Contains(q) || t.Text.ToLowerInvariant().Contains(q)) list.Items.Add(t);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            else view.Document = new FlowDocument(new Paragraph(new Run("Aucune rubrique ne contient « " + search.Text + " ».")));
        }

        void Select(string title)
        {
            if (string.IsNullOrEmpty(title)) return;
            search.Text = "";
            foreach (Topic t in list.Items)
                if (t.Title == title) { list.SelectedItem = t; list.ScrollIntoView(t); return; }
        }

        void Render(Topic t)
        {
            var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), PagePadding = new Thickness(0) };
            doc.Blocks.Add(new Paragraph(new Run(t.Title)) { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(31, 56, 100)), Margin = new Thickness(0, 0, 0, 10) });
            foreach (string raw in t.Text.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("## "))
                    doc.Blocks.Add(new Paragraph(new Run(line.Substring(3))) { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
                else if (line.StartsWith("- "))
                    doc.Blocks.Add(new Paragraph(new Run("•  " + line.Substring(2))) { Margin = new Thickness(18, 1, 0, 1) });
                else
                    doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 3, 0, 3) });
            }
            view.Document = doc;
        }

        static IEnumerable<Topic> Topics()
        {
            Func<string, string, Topic> T = (a, b) => new Topic { Title = a, Text = b };
            yield return T("Démarrage rapide",
@"Gestion GYM remplace le classeur Excel : tout se fait dans cette fenêtre, sans macro.
## Premiers pas
- Au premier lancement, crée la base de l'année : Fichier > Nouvelle base.
- Va dans l'onglet Configuration : renseigne les activités, les créneaux, la capacité des cours et les paramètres généraux.
- Onglet Packs (catalogue) : crée tes offres (nombre de crédits, prix, durée de validité).
- Onglet Clients : ajoute tes clients (bouton Nouveau client).
- Onglet Achats de packs : enregistre chaque vente. Onglet Présences : marque chaque venue.
## Principe
- Chaque saisie ouvre une fenêtre qui contrôle les données en direct : un message rouge bloque l'enregistrement, un message orange avertit sans bloquer.
- Les tableaux se trient en cliquant sur un en-tête de colonne.
- Les modifications restent en mémoire jusqu'à Ctrl+S. Un enregistrement de secours est fait toutes les 90 secondes dans un fichier de récupération.");
            yield return T("Dossiers et bases de données",
@"Au premier lancement, l'application crée à côté de l'exe cinq dossiers : Actuel, Archive, Export Excel, Export OpenOffice, Export PDF.
## Format
- Une base = un dossier de fichiers CSV (séparateur point-virgule, UTF-8). Tu peux les ouvrir avec Excel ou le Bloc-notes, mais modifie-les de préférence dans l'application.
- Actuel contient les bases en cours, Archive les années passées.
## Menu Fichier
- Nouvelle base : crée l'année en cours (ou une autre).
- Ouvrir une base : actuelle ou archive. Une archive s'ouvre en lecture seule.
- Enregistrer (Ctrl+S) : écrit la base dans Actuel. Les 10 dernières versions sont gardées dans le sous-dossier _sauvegardes.
- Enregistrer sous : copie la base dans Actuel sous une autre année.
- Archiver une copie : place une copie de la base ouverte dans Archive.
- Planifier une base pour l'année suivante : crée la base N+1 avec la configuration, les packs et éventuellement les clients, sans l'historique.
- Importer une configuration : reprend la configuration (activités, créneaux, paramètres fiscaux, charges fixes…) d'une autre base.
## Sécurité
- Un fichier verrou empêche d'ouvrir deux fois la même base en écriture.
- Si l'application se ferme brutalement, elle propose de restaurer le dernier enregistrement de secours au prochain lancement.");
            yield return T("Tableau de bord",
@"Vue d'ensemble de la base ouverte : chiffre d'affaires, nombre de clients actifs, abonnements à surveiller, répartition par activité, et graphiques mensuels.
- Les tuiles se mettent à jour à chaque modification.
- Les alertes (packs bientôt expirés, crédits épuisés) sont détaillées dans l'onglet Tracking abonnements.");
            yield return T("Clients",
@"Un client = identité, contact, âge, distance de son domicile, notes.
## Saisie
- Bouton Nouveau client, ou double-clic sur une ligne pour modifier.
- Le nom + prénom doit être unique : l'application avertit en cas de doublon probable.
- L'âge sert aux statistiques par tranche d'âge (rentabilité), la distance aux statistiques géographiques.
## Suppression
- Supprimer un client qui a des achats ou des présences est refusé pour éviter de casser l'historique : supprime ou transfère d'abord ses achats.");
            yield return T("Packs (catalogue)",
@"Le catalogue liste les offres : nom, catégorie, nombre de crédits, prix, durée de validité, famille.
- Le prix par séance est calculé automatiquement.
- Un pack déjà vendu ne peut pas être supprimé : désactive-le ou modifie-le avec prudence (les achats déjà enregistrés gardent leur prix d'origine).
- Les abonnements illimités se saisissent avec leur durée et un crédit de valeur 0 (illimité).");
            yield return T("Achats de packs",
@"Chaque vente d'un pack à un client.
## Saisie
- Choisis le client et le pack : le prix, la date de fin et le nombre de crédits sont proposés automatiquement mais restent modifiables.
- Le contrôle en direct avertit si le client a déjà un pack actif sur la même période ou si une date est incohérente.
## Suivi
- Les colonnes Crédits utilisés, Reste et Statut sont calculées à partir des présences rattachées.
- Les lignes en couleur signalent un pack expiré, épuisé ou en dépassement.");
            yield return T("Présences",
@"Une présence = un client venu à un créneau à une date donnée.
- Bouton Marquer une présence : choisis la date, le créneau, le client. Le pack est rattaché automatiquement (le plus proche de l'expiration), tu peux le changer.
- L'application refuse un doublon (même client, même créneau, même jour) et avertit si la capacité du cours est dépassée, si le pack est expiré ou n'a plus de crédit.
- Un jour peut être marqué férié, fermé ou vacances dans l'onglet Planning : il est alors signalé à la saisie.");
            yield return T("Planning",
@"Grille hebdomadaire des cours : activités, créneaux et jours.
- Les séances d'une journée peuvent être copiées vers d'autres jours (Données > Copier une journée).
- La nature du jour (normal, férié, vacances, fermé) est prise en compte dans les contrôles et le reporting.
- La capacité maximale par cours est réglée dans Configuration.");
            yield return T("Tracking abonnements",
@"Suivi des abonnements et packs : qui expire bientôt, qui n'a presque plus de crédits, qui n'est pas venu depuis longtemps.
- Le délai d'alerte (jours avant expiration) se règle dans Configuration.
- Clique sur un en-tête pour trier ; les lignes rouges sont les plus urgentes.
- Utile chaque semaine pour relancer les clients avant la fin de leur pack.");
            yield return T("Charges fixes",
@"Toutes les dépenses récurrentes de la salle : loyer, assurance, logiciels, abonnements…
## Saisie
- Libellé, catégorie, montant, fréquence (mensuelle, trimestrielle, annuelle, ponctuelle), date de début et de fin facultative.
- Le montant est ventilé automatiquement mois par mois sur l'année de la base.
## Utilisation
- Le total alimente le reporting et le calcul de rentabilité.
- Les montants ne sont pas déduits du chiffre d'affaires micro-entreprise pour l'URSSAF (le régime micro applique un abattement forfaitaire).");
            yield return T("URSSAF",
@"Estimation des cotisations sociales et de l'impôt de la micro-entreprise à partir du chiffre d'affaires encaissé.
- Les taux par catégorie d'activité (vente, prestation commerciale, prestation libérale…), l'ACRE, le versement libératoire et le barème de l'impôt se règlent via le bouton Paramètres.
- Les taux livrés par défaut sont ceux de 2026 : vérifie-les auprès de l'URSSAF et des impôts avant de t'y fier, ils changent régulièrement.
- Le calcul de l'impôt est simplifié (une seule personne, sans autres revenus). C'est une estimation de trésorerie, pas une déclaration.
- Le tableau mensuel ou trimestriel indique le CA à déclarer et les cotisations à provisionner.");
            yield return T("CFE",
@"Estimation de la Cotisation Foncière des Entreprises.
- Renseigne la commune, la base minimum et le barème dans Paramètres. Les tranches par défaut sont indicatives : remplace-les par celles de ta commune (avis CFE ou site impots.gouv.fr).
- L'application applique l'exonération de première année, les réductions éventuelles et calcule l'acompte de décembre quand le seuil est dépassé.
- Vérifie toujours le montant réel sur ton avis d'imposition.");
            yield return T("Rentabilité clients",
@"Analyse de ce que chaque client rapporte : chiffre d'affaires, nombre de venues, prix moyen par séance, ancienneté, fidélité.
- Les clients sont classés en segments (fidèles, à risque, nouveaux…) selon les seuils réglables dans Paramètres.
- Des tableaux par tranche d'âge et par distance aident à cibler la communication.
- Les réinvestissements conseillés (nouveaux clients, clients existants) sont calculés à partir des pourcentages que tu fixes.");
            yield return T("Reporting",
@"Synthèse chiffrée et graphiques de l'année : chiffre d'affaires mensuel, charges, résultat, fréquentation, répartition par activité et par pack.
- Tout ce qui est affiché ici est exactement ce qui sort dans les exports Excel, OpenOffice et PDF.
- Lance l'export depuis le bouton de l'onglet ou le menu Fichier > Exporter.");
            yield return T("Configuration",
@"Paramètres généraux de la base : année, activités, créneaux, capacités, familles de packs, délais d'alerte, jours fériés.
- Chaque liste est modifiable ; renommer une activité la renomme partout dans l'historique.
- Supprimer une activité ou un créneau utilisé est refusé.
- Les paramètres fiscaux (URSSAF, CFE, rentabilité) se règlent dans leurs onglets respectifs.");
            yield return T("Exporter (Excel, OpenOffice, PDF)",
@"Menu Fichier > Exporter.
## Contenu
- Toutes les données (clients, packs, achats, présences, planning, charges), les calculs (URSSAF, CFE, rentabilité, tracking, reporting), les graphiques et la configuration.
## Formats
- Excel (.xlsx) : une feuille par rubrique, en-têtes mis en forme, largeurs ajustées, graphiques natifs.
- OpenOffice / LibreOffice (.ods) : même structure, graphiques natifs.
- PDF : un tableau par page, titre et numéro de page sur chaque page, en A3 ou A4 paysage, graphiques inclus.
## Où sont les fichiers ?
- Dans Export Excel, Export OpenOffice ou Export PDF, à côté de l'application. Le nom contient la date et l'heure : aucun export n'écrase le précédent.
## Si l'export échoue
- Message « fichier verrouillé » : ferme le fichier dans Excel puis recommence (le nom change à chaque minute, ce cas est rare).
- Les modifications non enregistrées sont exportées telles qu'elles sont à l'écran.");
            yield return T("Importer (Excel, CSV, XLSM, ODS)",
@"Menu Fichier > Importer un fichier.
## Formats lus
- .xlsx, .xlsm, .xltx, .ods, .csv, .txt. Pour un ancien .xls ou .xlsb, ouvre-le dans Excel et enregistre-le en .xlsx.
- Les macros d'un .xlsm sont ignorées : seules les données sont lues.
## Déroulement
- Chaque feuille est analysée : l'application reconnaît le type de données (clients, packs, achats, présences, charges, planning) grâce aux en-têtes de colonnes, même avec des accents ou des noms un peu différents.
- Tu vérifies le type reconnu, tu peux le changer, voir un aperçu, ou ignorer la feuille.
- Les données sont converties en CSV (copie dans Actuel\_import), puis ajoutées à la base ouverte comme si tu les avais saisies : tout reste modifiable.
## Garde-fous
- Chaque ligne passe par les mêmes contrôles que la saisie manuelle : les lignes invalides sont rejetées et listées dans le rapport avec la raison.
- Les doublons sont ignorés.
- Rien n'est écrit sur disque avant Ctrl+S : ferme sans enregistrer pour annuler un import.
- Impossible d'importer dans une base en lecture seule ou archivée.");
            yield return T("Sauvegardes et récupération",
@"- Chaque Ctrl+S garde une copie des 10 versions précédentes dans le dossier _sauvegardes de la base.
- Toutes les 90 secondes, un enregistrement de secours est écrit dans _recuperation. Après un plantage ou une coupure de courant, l'application propose de le restaurer.
- Pour restaurer manuellement une ancienne version : ferme l'application, copie les fichiers du sous-dossier choisi dans _sauvegardes vers le dossier de la base.
- Conseil : copie régulièrement le dossier de l'application sur une clé USB ou un cloud.");
            yield return T("Raccourcis clavier",
@"- Ctrl+S : enregistrer
- Ctrl+N : nouvelle base
- Ctrl+O : ouvrir une base
- F5 : actualiser
- F1 : cette aide (rubrique de l'onglet affiché)
- Échap : fermer une fenêtre de saisie
- Clic sur un en-tête de colonne : trier
- Double-clic sur une ligne : modifier");
            yield return T("Dépannage",
@"## L'application ne démarre pas
- Windows 10 contient .NET Framework 4.8 : aucune installation n'est nécessaire. Si Windows signale un fichier bloqué, clic droit sur l'exe > Propriétés > Débloquer.
## « Base verrouillée »
- La base est ouverte dans une autre fenêtre, ou l'application s'est fermée brutalement. Si tu es sûr qu'elle n'est pas ouverte ailleurs, accepte de passer outre au message proposé.
## Impossible d'écrire
- Le dossier est en lecture seule (Program Files, dossier synchronisé verrouillé). Déplace l'application dans Documents, par exemple.
## Un chiffre semble faux
- Vérifie d'abord les dates des achats et des présences, puis les paramètres fiscaux de l'onglet concerné.
## Une erreur inattendue
- Elle est inscrite dans le fichier erreurs.log à côté de l'application : envoie-le pour diagnostic.");
        }
    }
}
