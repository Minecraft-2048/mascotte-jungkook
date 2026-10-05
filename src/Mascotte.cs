// Mascottes de bureau, dans l'esprit des pets de Codex : une fenêtre transparente toujours visible,
// posée en bas à droite de l'écran. Le même code sert à toutes les mascottes ; ce qui les distingue
// (images, nom, action au clic, sauts sur les fenêtres…) vient des ressources intégrées à chaque exe.
// Compilation : construire.ps1 (csc du .NET Framework, aucune dépendance à installer).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MascotteClaude
{
    static class Programme
    {
        [STAThread]
        static int Main(string[] args)
        {
            // MascotteClaude.exe --etat running|waiting|review|failed|idle|waving|jumping|walking [message]
            // transmet un état à la mascotte déjà lancée (pratique depuis un hook Claude Code).
            if (args.Length >= 2 && (args[0] == "--etat" || args[0] == "--state"))
            {
                Directory.CreateDirectory(Perso.Dossier);
                string message = string.Join(" ", args, 2, args.Length - 2);
                File.WriteAllText(Perso.FichierEtat, args[1] + "\n" + message);
                return 0;
            }
            return Lancer();
        }

        // À part de Main : un hook qui ne fait qu'envoyer un état ne charge ainsi jamais WPF.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Lancer()
        {
            bool premiere;
            using (new Mutex(true, "Mascotte" + Perso.Id + "-Instance", out premiere))
            {
                if (!premiere) return 0;
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += (s, e) =>
                {
                    try { File.AppendAllText(Path.Combine(Perso.Dossier, "erreurs.log"), DateTime.Now + " " + e.Exception + "\r\n"); }
                    catch (IOException) { }
                    e.Handled = true;
                };
                app.Run(new Mascotte());
            }
            return 0;
        }
    }

    // Fiche du personnage (perso.txt, intégrée à l'exe). Aucun type WPF ici : un hook qui envoie
    // un état ne lit que cette classe.
    static class Perso
    {
        static readonly Dictionary<string, string> fiche = Charger();

        public static readonly string Id = Lire("id", "Claude");
        public static readonly string Nom = Lire("nom", Id);
        public static readonly string Bonjour = Lire("bonjour", "Bonjour !");
        public static readonly string Role = Lire("action", "parole");       // bouton de la pilule : claude, lien, plateforme ou parole
        public static readonly string Titre = Lire("titre", Nom);
        public static readonly string Glyphe = ((char)Convert.ToInt32(Lire("glyphe", "E70F"), 16)).ToString();
        public static readonly string Lien = Lire("lien", "");
        public static readonly string Clic = Lire("clic", "geste");          // geste, lien, saut ou parole
        public static readonly string[] Paroles = Lire("paroles", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        public static readonly bool Plateformes = Lire("plateformes", "0") == "1";
        public static readonly bool Sieste = Lire("sieste", "0") == "1";
        public static readonly double Droite = double.Parse(Lire("droite", "230"), CultureInfo.InvariantCulture);
        public static readonly double Echelle = double.Parse(Lire("echelle", "0.75"), CultureInfo.InvariantCulture);
        public static readonly string Appli = Lire("appli", "");             // raccourci du menu Démarrer à ouvrir plutôt que le lien
        public static readonly string Travail = Lire("travail", "Travailler");   // nom de l'animation « travail » dans le menu
        public static readonly bool Musique = Lire("musique", "0") == "1";   // danse et chante quand le PC joue de la musique
        public static readonly string[] Bisous = Liste("bisous", "Mwah ! ♥");
        public static readonly string[] Coeurs = Liste("coeurs", "♥");
        public static readonly string[] Chansons = Liste("chansons", "♪ La la la ♪");
        public static readonly string[] Danses = Liste("danse", "♪ On danse ?");
        public static readonly string[] Tenues = Liste("tenues", "");      // noms des tenues : atlas.png, puis tenue1.png, tenue2.png…

        public static readonly string Dossier = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mascotte" + Id);
        public static readonly string FichierEtat = Path.Combine(Dossier, "etat.txt");
        public static readonly string FichierReglages = Path.Combine(Dossier, "reglages.txt");

        static Dictionary<string, string> Charger()
        {
            var valeurs = new Dictionary<string, string>();
            using (Stream flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("perso.txt"))
            {
                if (flux == null) return valeurs;
                using (var lecteur = new StreamReader(flux, Encoding.UTF8))
                {
                    string ligne;
                    while ((ligne = lecteur.ReadLine()) != null)
                    {
                        int egal = ligne.IndexOf('=');
                        if (egal > 0) valeurs[ligne.Substring(0, egal).Trim()] = ligne.Substring(egal + 1).Trim();
                    }
                }
            }
            return valeurs;
        }

        static string Lire(string cle, string defaut)
        {
            string valeur;
            return fiche.TryGetValue(cle, out valeur) ? valeur : defaut;
        }

        static string[] Liste(string cle, string defaut)
        {
            return Lire(cle, defaut).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    sealed class Animation
    {
        public readonly int Ligne;
        public readonly int[] Colonnes;
        public readonly int[] Durees;

        public Animation(int ligne, int[] colonnes, int[] durees)
        {
            Ligne = ligne; Colonnes = colonnes; Durees = durees;
        }

        // Ligne lue de gauche à droite, la dernière image tenue un peu plus longtemps (rythme des pets Codex).
        public static Animation Suite(int ligne, int nombre, int duree, int derniere)
        {
            var colonnes = new int[nombre];
            var durees = new int[nombre];
            for (int i = 0; i < nombre; i++) { colonnes[i] = i; durees[i] = duree; }
            durees[nombre - 1] = derniere;
            return new Animation(ligne, colonnes, durees);
        }
    }

    // L'oreille : écoute le niveau de la sortie audio de Windows (crête-mètre Core Audio, sans rien
    // enregistrer), en déduit s'il y a de la musique, son tempo et la place des temps. Elle tourne sur
    // son propre fil et prévient la mascotte à chaque changement et à chaque demi-temps.
    sealed class Oreille
    {
        const double Seau = 0.02;                // un point d'enveloppe toutes les 20 ms
        const int Memoire = 300;                 // 6 s d'enveloppe
        const int RetardMin = 17, RetardMax = 50;   // périodes cherchées : de 176 à 60 battements par minute

        readonly float[] enveloppe = new float[Memoire];
        long remplis;                            // seaux remplis depuis le début (rangés en cercle)
        readonly Action<bool, double> changement;   // (musique ou pas, durée d'un temps en secondes)
        readonly Action<bool> demiTemps;         // vrai sur le temps, faux entre deux temps
        public volatile bool Active = true;

        bool musique;
        int oui, non;                            // analyses de suite qui disent « musique » ou « plus de musique »
        double periode = 0.5, prochainDemi, dernierDemi;
        bool prochainFort;

        public Oreille(Action<bool, double> changement, Action<bool> demiTemps)
        {
            this.changement = changement;
            this.demiTemps = demiTemps;
            var fil = new Thread(Boucle) { IsBackground = true, Name = "Oreille", Priority = ThreadPriority.AboveNormal };
            fil.Start();
        }

        void Boucle()
        {
            IAudioMeterInformation metre = null;
            var montre = Stopwatch.StartNew();
            double rouvrir = 0, analyse = 0;
            long seau = -1;
            float crete = 0;
            while (true)
            {
                Thread.Sleep(Active ? 8 : 400);
                double t = montre.Elapsed.TotalSeconds;
                if (!Active)
                {
                    if (musique) Annoncer(false);
                    remplis = 0;
                    seau = -1;
                    continue;
                }
                // la sortie par défaut peut changer (casque branché) : on la reprend de temps en temps
                if (metre == null || (t >= rouvrir && !musique))
                {
                    if (metre != null) Marshal.ReleaseComObject(metre);
                    metre = Ouvrir();
                    rouvrir = t + 10;
                    if (metre == null) { rouvrir = t + 5; Thread.Sleep(2000); continue; }
                }
                float valeur;
                if (metre.GetPeakValue(out valeur) != 0) { Marshal.ReleaseComObject(metre); metre = null; continue; }

                long s = (long)(t / Seau);
                if (s != seau)
                {
                    if (seau >= 0)
                        for (long k = seau; k < s && k < seau + Memoire; k++) Ajouter(crete);   // un seau sauté garde la valeur du précédent
                    seau = s;
                    crete = 0;
                }
                crete = Math.Max(crete, valeur);

                if (t >= analyse)
                {
                    analyse = t + 0.5;
                    Analyser(seau * Seau);
                }
                if (musique && t >= prochainDemi)
                {
                    dernierDemi = prochainDemi;
                    prochainDemi += periode / 2;
                    if (prochainDemi < t) prochainDemi = t + periode / 2;   // le fil a pris du retard : on ne rattrape pas
                    bool fort = prochainFort;
                    prochainFort = !prochainFort;
                    demiTemps(fort);
                }
            }
        }

        void Ajouter(float valeur)
        {
            enveloppe[remplis % Memoire] = valeur;
            remplis++;
        }

        // maintenant = début du seau en cours, qui n'est pas encore dans l'enveloppe
        void Analyser(double maintenant)
        {
            int n = (int)Math.Min(remplis, Memoire);
            if (n < 150) return;                                  // moins de 3 s d'écoute
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Sqrt(enveloppe[(remplis - n + i) % Memoire]);

            // du son presque sans interruption pendant les 3 dernières secondes (une ambiance très
            // faible, le bruit de fond d'un jeu, reste sous le seuil : crête de 0,035)
            int sonores = 0;
            for (int i = n - 150; i < n; i++) if (x[i] > 0.187) sonores++;
            double continu = sonores / 150.0;

            // attaques : ce qui dépasse la moyenne des 60 ms précédentes
            var o = new double[n];
            double somme = 0;
            for (int i = 3; i < n; i++) { o[i] = Math.Max(0, x[i] - (x[i - 1] + x[i - 2] + x[i - 3]) / 3); somme += o[i]; }
            double moyenne = somme / n, energie = 0;
            for (int i = 0; i < n; i++) { o[i] -= moyenne; energie += o[i] * o[i]; }
            if (energie < 1e-6) { Decider(false, continu, 0, periode); return; }

            // tempo : autocorrélation des attaques, un peu favorisée autour de 110 battements par minute
            var ac = new double[RetardMax + 2];
            for (int r = RetardMin - 1; r <= RetardMax + 1; r++)
            {
                double a = 0;
                for (int i = r; i < n; i++) a += o[i] * o[i - r];
                ac[r] = a / energie;
            }
            int meilleur = RetardMin;
            double score = double.MinValue;
            for (int r = RetardMin; r <= RetardMax; r++)
            {
                double ecart = Math.Log(r / 27.0, 2) / 1.2;
                double note = ac[r] * Math.Exp(-0.5 * ecart * ecart);
                if (note > score) { score = note; meilleur = r; }
            }
            // une musique hésite souvent entre deux tempos (ici 94 et 141) : tant que celui du moment
            // tient presque aussi bien, on le garde, sinon la danse changerait de vitesse sans arrêt
            int actuel = (int)Math.Round(periode / Seau);
            if (musique && actuel > RetardMin && actuel < RetardMax)
            {
                int voisin = actuel;
                for (int r = actuel - 1; r <= actuel + 1; r++) if (ac[r] > ac[voisin]) voisin = r;
                double ecart = Math.Log(voisin / 27.0, 2) / 1.2;
                if (ac[voisin] * Math.Exp(-0.5 * ecart * ecart) >= 0.7 * score) meilleur = voisin;
            }
            double confiance = ac[meilleur];
            // période plus fine que le seau : sommet de la parabole qui passe par les trois points
            double gauche = ac[meilleur - 1], milieu = ac[meilleur], droite = ac[meilleur + 1];
            double courbure = gauche - 2 * milieu + droite;
            double p = meilleur + (courbure < 0 ? Math.Max(-0.5, Math.Min(0.5, 0.5 * (gauche - droite) / courbure)) : 0);

            bool dejaLa = musique;
            Decider(continu > 0.75 && confiance > 0.25, continu, confiance, p * Seau);
            if (!musique) return;

            // place des temps : le décalage qui fait tomber le plus d'attaques sur la grille de période p
            int pas = (int)Math.Round(p);
            int phase = 0;
            double meilleurePhase = double.MinValue;
            for (int f = 0; f < pas; f++)
            {
                double s = 0;
                for (double i = n - 1 - f; i >= 0; i -= p) s += o[(int)Math.Round(i)];
                if (s > meilleurePhase) { meilleurePhase = s; phase = f; }
            }
            double nouvellePeriode = p * Seau;
            double dernierTemps = maintenant - (phase + 0.5) * Seau;
            // le prochain demi-temps sur la nouvelle grille, sans en jouer deux presque à la suite
            double demi = nouvellePeriode / 2;
            double suivant = dernierTemps + Math.Ceiling((maintenant - dernierTemps) / demi) * demi;
            if (suivant - dernierDemi < demi * 0.5) suivant += demi;
            bool changeTempo = dejaLa && Math.Abs(nouvellePeriode - periode) > 0.03;
            periode = nouvellePeriode;
            prochainDemi = suivant;
            prochainFort = Math.Abs(((suivant - dernierTemps) / nouvellePeriode) - Math.Round((suivant - dernierTemps) / nouvellePeriode)) < 0.25;
            if (changeTempo) changement(true, periode);
            Trace("tempo " + (60 / periode).ToString("0") + " bpm, confiance " + confiance.ToString("0.00") + ", continu " + continu.ToString("0.00"));
        }

        // Il faut trois analyses de suite (1,5 s) pour commencer à danser, et quatre (2 s) pour s'arrêter.
        void Decider(bool entendue, double continu, double confiance, double nouvellePeriode)
        {
            if (entendue) { oui++; non = 0; } else { non++; oui = 0; }
            if (!musique && oui >= 3)
            {
                Trace("musique ! continu " + continu.ToString("0.00") + " confiance " + confiance.ToString("0.00"));
                periode = nouvellePeriode;
                Annoncer(true);
            }
            else if (musique && non >= 4 && continu < 0.6)
            {
                Trace("fin de la musique");
                Annoncer(false);
            }
        }

        void Annoncer(bool active)
        {
            musique = active;
            oui = non = 0;
            if (active) { dernierDemi = 0; prochainDemi = double.MaxValue; }
            changement(active, periode);
        }

        static IAudioMeterInformation Ouvrir()
        {
            try
            {
                var enumerateur = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice appareil;
                if (enumerateur.GetDefaultAudioEndpoint(0, 1, out appareil) != 0) return null;   // sortie, multimédia
                Guid iid = typeof(IAudioMeterInformation).GUID;
                object metre;
                if (appareil.Activate(ref iid, 23, IntPtr.Zero, out metre) != 0) return null;
                return (IAudioMeterInformation)metre;
            }
            catch (Exception) { return null; }                   // pas de carte son, service audio arrêté…
        }

        static readonly bool traceActive = File.Exists(Path.Combine(Perso.Dossier, "trace.on"));

        static void Trace(string texte)
        {
            if (!traceActive) return;
            try { File.AppendAllText(Path.Combine(Perso.Dossier, "trace.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + texte + "\r\n"); }
            catch (IOException) { }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints();                             // pas utilisée : simple place dans la table
            [PreserveSig] int GetDefaultAudioEndpoint(int flux, int role, out IMMDevice appareil);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int contexte, IntPtr parametres, [MarshalAs(UnmanagedType.IUnknown)] out object obtenu);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float crete);
        }
    }

    sealed class Mascotte : Window
    {
        // Atlas : 8 colonnes de cases 192x208. Les 9 premières lignes suivent la disposition des pets
        // Codex ; la dixième (facultative) donne la pose en l'air, tournée à droite puis à gauche.
        // Les quatre suivantes, facultatives aussi, sont les gestes de star : bisou, cœur, danse, chant.
        const int CaseL = 192, CaseH = 208, Lignes = 14, Colonnes = 8;
        const int LigneBisou = 10, LigneCoeur = 11, LigneDanse = 12, LigneChant = 13;
        const double PiedsCase = 200;        // bas des pieds dans la case
        const double ZoneBulle = 46, ZonePilule = 44, LargeurMin = 210;
        const double BasDefaut = 46;
        static readonly double DroiteDefaut = Perso.Droite;

        static readonly string Dossier = Perso.Dossier, FichierEtat = Perso.FichierEtat, FichierReglages = Perso.FichierReglages;
        const string CleDemarrage = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static readonly string NomDemarrage = "Mascotte" + Perso.Id;

        static readonly Brush Encre = Pinceau(0x3D, 0x39, 0x29);
        static readonly Brush Creme = Pinceau(0xFA, 0xF9, 0xF5);
        static readonly Brush Trait = Pinceau(0xE4, 0xDF, 0xD0);
        static readonly Brush Survol = Pinceau(0xF0, 0xEE, 0xE6);

        readonly Animation respiration = new Animation(0, new[] { 0, 1, 2, 3 }, new[] { 620, 170, 620, 170 });
        readonly Animation clin = new Animation(0, new[] { 4 }, new[] { 140 });
        readonly Animation marcheDroite = Animation.Suite(1, 8, 120, 120);
        readonly Animation marcheGauche = Animation.Suite(2, 8, 120, 120);
        readonly Animation salut = Animation.Suite(3, 4, 140, 280);
        readonly Animation saut = Animation.Suite(4, 5, 140, 280);
        readonly Animation rate = Animation.Suite(5, 8, 140, 1400);
        readonly Animation attente = Animation.Suite(6, 6, 150, 260);
        readonly Animation travail = Animation.Suite(7, 6, 120, 220);
        readonly Animation revue = Animation.Suite(8, 6, 150, 280);
        readonly Animation sieste = new Animation(8, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 900, 350, 900, 900, 350, 900 });
        readonly Animation courseDroite = Animation.Suite(1, 8, 70, 70);
        readonly Animation courseGauche = Animation.Suite(2, 8, 70, 70);
        readonly Animation reception = new Animation(4, new[] { 4 }, new[] { 170 });
        readonly Animation bisou = new Animation(LigneBisou, new[] { 0, 1, 2, 3 }, new[] { 240, 260, 380, 650 });
        readonly Animation coeur = new Animation(LigneCoeur, new[] { 0, 1, 0, 2, 3, 2 }, new[] { 300, 300, 300, 330, 330, 520 });

        enum Mode { Repos, Geste, Marche, Glisse, Agent, Saut, Danse }
        enum Agent { Aucun, Travail, Attente }

        // Une forme = un atlas complet. La plupart des mascottes n'en ont qu'une ; Mario en a trois
        // (petit, grand, de feu) et passe de l'une à l'autre en attrapant ce qui sort du bloc.
        readonly List<BitmapSource[,]> formes = new List<BitmapSource[,]>();
        readonly List<double> tetes = new List<double>();
        BitmapSource[,] images;          // l'atlas de la forme du moment
        BitmapSource[] objets;           // tuiles du bloc et de son butin, si le personnage en a
        BitmapSource[] dessins;          // cœurs et notes de musique, si le personnage en a
        int forme, tenue;
        MenuItem menuTenues;
        readonly Random hasard = new Random();
        Image sprite;
        Border bulle, pilule;
        TextBlock texteBulle;
        ContextMenu menu;

        Animation anim;
        int index, tours;
        Action fin;
        Mode mode = Mode.Repos;
        Agent agent = Agent.Aucun;

        double echelle = Perso.Echelle;
        bool balade = true, premierPlan = true;
        bool coeurs = true;              // cœurs et bisous : décochés, plus aucun cœur ne s'envole et elle n'en fait plus d'elle-même
        Point maison, ancre;             // point au sol sous la mascotte : sa place habituelle, et sa place du moment
        double cible;
        DateTime dernierPas;
        Action apresMarche;              // ce qu'elle fait en arrivant (elle court alors au lieu de marcher)

        // Fenêtres-plateformes : le haut d'une fenêtre sert de sol (voir la fin du fichier).
        IntPtr poignee, support, supportVise;   // support = fenêtre sur laquelle elle se tient, Zero = le sol
        double supportX;                 // sa place le long du bord, depuis la gauche de la fenêtre
        Point departSaut, arriveeSaut;
        double hauteurSaut, dureeSaut;
        DateTime debutSaut;
        bool chute;

        Point appuiCurseur, appuiAncre;
        bool glisse;
        int sensGlisse, clics;
        double dernierX;

        string dernierEtat;
        DateTime heureEtat;
        FileSystemWatcher guetteur;

        readonly DispatcherTimer horloge, minuteurClin, minuteurHasard, minuteurMarche, minuteurBulle, minuteurPilule, minuteurAgent,
            minuteurSaut, minuteurSupport, minuteurBloc, minuteurPouvoir, minuteurParticules;

        // Musique (voir « danse et chant ») : ce que dit l'oreille, et si on la laisse danser.
        Oreille oreille;
        bool musique, dansePermise = true;
        double periode = 0.5;            // durée d'un temps, en secondes
        int phrase;

        public Mascotte()
        {
            Title = "Mascotte " + Perso.Nom;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;

            Directory.CreateDirectory(Dossier);
            LireReglages();
            ChargerImages();
            ConstruireInterface();
            ConstruireMenu();
            Topmost = premierPlan;

            horloge = Minuteur(100, Avancer);
            minuteurClin = Minuteur(3000, Cligner);
            minuteurHasard = Minuteur(30000, (s, e) =>
            {
                // pas de fantaisie pendant qu'on s'occupe d'elle : elle ne doit pas filer sous le curseur
                if (mode == Mode.Repos && !sprite.IsMouseOver && !pilule.IsMouseOver && !menu.IsOpen) GesteAuHasard();
            });
            minuteurMarche = Minuteur(16, Pas);
            minuteurBulle = Minuteur(3000, (s, e) => CacherBulle());
            minuteurPilule = Minuteur(700, (s, e) => CacherPilule());
            // si l'état « travail » ou « attente » n'est jamais levé, on finit par revenir au repos
            minuteurAgent = Minuteur(20 * 60 * 1000, (s, e) => AppliquerEtat("idle", ""));
            minuteurSaut = Minuteur(16, Vol);
            minuteurSupport = Minuteur(40, SuivreSupport);
            minuteurBloc = Minuteur(16, AnimerBloc);
            minuteurPouvoir = Minuteur(DureePouvoir, FinDuPouvoir);
            minuteurParticules = Minuteur(16, AnimerParticules);

            Loaded += (s, e) =>
            {
                AppliquerEchelle();
                minuteurClin.Start();
                if (Perso.Plateformes) minuteurSupport.Start();
                Faire(salut, 2, Perso.Bonjour);
                SurveillerEtat();
                if (Perso.Musique && AGeste(LigneDanse))
                    oreille = new Oreille(
                        (active, duree) => Dispatcher.BeginInvoke(new Action(() => Musique(active, duree))),
                        fort => Dispatcher.BeginInvoke(new Action(() => DemiTemps(fort)))) { Active = dansePermise };
            };
            Closed += (s, e) =>
            {
                if (guetteur != null) guetteur.Dispose();
                if (bloc != null) { bloc.Close(); butin.Close(); }
                foreach (Particule p in particules) p.Fenetre.Close();
            };
        }

        // ------------------------------------------------------------------ images

        void ChargerImages()
        {
            // Un fichier mascotte.png posé à côté de l'exe remplace l'atlas intégré.
            // atlas2.png, atlas3.png : les autres formes du personnage (grand Mario, Mario de feu).
            // Pour un personnage qui a des tenues, la première forme est la tenue choisie.
            Assembly moi = Assembly.GetExecutingAssembly();
            string externe = Path.Combine(Path.GetDirectoryName(moi.Location), "mascotte.png");
            if (!TenueExiste(tenue)) tenue = 0;
            foreach (string nom in new[] { NomTenue(tenue), "atlas2.png", "atlas3.png" })
                using (Stream flux = nom == "atlas.png" && File.Exists(externe) ? (Stream)File.OpenRead(externe) : moi.GetManifestResourceStream(nom))
                {
                    if (flux == null) continue;
                    int tete;
                    formes.Add(Atlas(flux, out tete));
                    tetes.Add(tete);
                }
            images = formes[0];

            // objets.png : bande de tuiles carrées (bloc ?, bloc vide, champignon, pièce, fleur)
            objets = Tuiles(moi, "objets.png");
            // particules.png : cœurs (rose, violet, rouge) et notes de musique qui s'envolent
            dessins = Tuiles(moi, "particules.png");
            GC.Collect();                                        // les atlas décodés ne servent plus
        }

        static BitmapSource[] Tuiles(Assembly moi, string nom)
        {
            using (Stream flux = moi.GetManifestResourceStream(nom))
            {
                if (flux == null) return null;
                int largeur, hauteur;
                byte[] tout = Decoder(flux, out largeur, out hauteur);
                var tuiles = new BitmapSource[largeur / hauteur];
                for (int i = 0; i < tuiles.Length; i++) tuiles[i] = Morceau(tout, largeur, i * hauteur, 0, hauteur, hauteur);
                return tuiles;
            }
        }

        bool AGeste(int ligne)
        {
            return images[ligne, 0] != null;
        }

        static BitmapSource[,] Atlas(Stream flux, out int tete)
        {
            int largeur, hauteur;
            byte[] tout = Decoder(flux, out largeur, out hauteur);
            var cases = new BitmapSource[Lignes, Colonnes];
            for (int l = 0; l < Lignes && (l + 1) * CaseH <= hauteur; l++)
                for (int c = 0; c < Colonnes && (c + 1) * CaseL <= largeur; c++)
                    cases[l, c] = Morceau(tout, largeur, c * CaseL, l * CaseH, CaseL, CaseH);
            // haut de la tête au repos : première ligne non vide de la première case
            tete = 0;
            while (tete < CaseH && LigneVide(tout, largeur, tete, CaseL)) tete++;
            return cases;
        }

        // Une tenue = un atlas complet. Seule celle qu'il porte est décodée : chacune pèse une quinzaine de Mo.
        static string NomTenue(int numero)
        {
            return numero == 0 ? "atlas.png" : "tenue" + numero + ".png";
        }

        static bool TenueExiste(int numero)
        {
            if (numero == 0) return true;
            return numero > 0 && numero < Perso.Tenues.Length
                && Assembly.GetExecutingAssembly().GetManifestResourceInfo(NomTenue(numero)) != null;
        }

        void ChangerTenue(int numero)
        {
            if (numero == tenue || !TenueExiste(numero) || mode == Mode.Saut) return;
            int tete;
            using (Stream flux = Assembly.GetExecutingAssembly().GetManifestResourceStream(NomTenue(numero)))
                formes[0] = Atlas(flux, out tete);
            tetes[0] = tete;
            tenue = numero;
            images = formes[forme];
            GC.Collect();                                        // l'ancienne tenue ne sert plus
            AppliquerEchelle();
            Enregistrer();
            if (menuTenues != null)
                foreach (MenuItem choix in menuTenues.Items) choix.IsChecked = (int)choix.Tag == numero;
            Faire(salut, 2, "Tenue « " + Perso.Tenues[numero] + " » !");
            if (dessins != null) PetitsCoeurs();
        }

        static byte[] Decoder(Stream flux, out int largeur, out int hauteur)
        {
            var decodeur = new PngBitmapDecoder(flux, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var image = new FormatConvertedBitmap(decodeur.Frames[0], PixelFormats.Pbgra32, null, 0);
            largeur = image.PixelWidth;
            hauteur = image.PixelHeight;
            var tout = new byte[largeur * 4 * hauteur];
            image.CopyPixels(tout, largeur * 4, 0);
            return tout;
        }

        static BitmapSource Morceau(byte[] tout, int largeurTotale, int x, int y, int l, int h)
        {
            var pixels = new byte[l * 4 * h];
            for (int ligne = 0; ligne < h; ligne++)
                Buffer.BlockCopy(tout, ((y + ligne) * largeurTotale + x) * 4, pixels, ligne * l * 4, l * 4);
            BitmapSource image = BitmapSource.Create(l, h, 96, 96, PixelFormats.Pbgra32, null, pixels, l * 4);
            image.Freeze();
            return image;
        }

        static bool LigneVide(byte[] tout, int largeurTotale, int y, int l)
        {
            for (int x = 0; x < l; x++)
                if (tout[(y * largeurTotale + x) * 4 + 3] != 0) return false;
            return true;
        }

        // --------------------------------------------------------------- interface

        void ConstruireInterface()
        {
            var grille = new Grid();
            grille.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ZoneBulle) });
            grille.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grille.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ZonePilule) });

            sprite = new Image { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand };
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
            Grid.SetRow(sprite, 1);
            sprite.MouseLeftButtonDown += Appui;
            sprite.MouseMove += Deplacement;
            sprite.MouseLeftButtonUp += Relache;
            sprite.MouseEnter += (s, e) => MontrerPilule();
            sprite.MouseLeave += (s, e) => minuteurPilule.Start();
            grille.Children.Add(sprite);

            texteBulle = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, Foreground = Encre,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = LargeurMin - 36
            };
            bulle = new Border
            {
                Background = Creme, BorderBrush = Trait, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 5, 10, 6), Child = texteBulle, Effect = Ombre(),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = 0, IsHitTestVisible = false
            };
            Grid.SetRow(bulle, 0);
            grille.Children.Add(bulle);

            // La pilule qui apparaît sous la mascotte au survol, comme celle de Codex.
            var boutons = new StackPanel { Orientation = Orientation.Horizontal };
            boutons.Children.Add(Bouton(Perso.Glyphe, Perso.Titre, ActionPrincipale));
            boutons.Children.Add(new Border { Width = 1, Height = 16, Background = Trait, Margin = new Thickness(3, 0, 3, 0) });
            boutons.Children.Add(Bouton("", "Menu de la mascotte", OuvrirMenu));
            pilule = new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(17), Padding = new Thickness(5, 3, 5, 3),
                Child = boutons, Effect = Ombre(), Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0, IsHitTestVisible = false
            };
            Grid.SetRow(pilule, 2);
            pilule.MouseEnter += (s, e) => MontrerPilule();
            pilule.MouseLeave += (s, e) => minuteurPilule.Start();
            grille.Children.Add(pilule);

            Content = grille;
        }

        Border Bouton(string glyphe, string aide, Action clic)
        {
            var bouton = new Border
            {
                Width = 32, Height = 28, CornerRadius = new CornerRadius(14), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, ToolTip = aide,
                Child = new TextBlock
                {
                    Text = glyphe, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15,
                    Foreground = Encre, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            bouton.MouseEnter += (s, e) => bouton.Background = Survol;
            bouton.MouseLeave += (s, e) => bouton.Background = Brushes.Transparent;
            bouton.MouseLeftButtonUp += (s, e) => { clic(); e.Handled = true; };
            return bouton;
        }

        void ConstruireMenu()
        {
            menu = new ContextMenu();
            menu.Items.Add(Element(Perso.Titre, ActionPrincipale));
            if (Perso.Plateformes && Perso.Role != "plateforme") menu.Items.Add(Element("Sauter sur une fenêtre", SauterSurFenetre));
            if (Perso.Plateformes) menu.Items.Add(Element("Redescendre au sol", Descendre));
            if (objets != null) menu.Items.Add(Element("Faire apparaître un bloc « ? »", Bloc));
            menu.Items.Add(new Separator());

            var animations = new MenuItem { Header = "Animations" };
            if (AGeste(LigneBisou)) animations.Items.Add(Element("Envoyer un bisou", Bisou));
            if (AGeste(LigneCoeur)) animations.Items.Add(Element("Faire un cœur", Coeur));
            if (AGeste(LigneDanse)) animations.Items.Add(Element("Danser", Danser));
            if (AGeste(LigneChant)) animations.Items.Add(Element("Chanter", Chanter));
            if (animations.Items.Count > 0) animations.Items.Add(new Separator());
            animations.Items.Add(Element("Saluer", () => Faire(salut, 2, null)));
            animations.Items.Add(Element("Sauter", () => SautSurPlace(null)));
            animations.Items.Add(Element("Se promener", Balade));
            animations.Items.Add(Element(Perso.Travail, () => Faire(travail, 5, null)));
            animations.Items.Add(Element("Attendre une réponse", () => Faire(attente, 3, null)));
            if (Perso.Sieste) animations.Items.Add(Element("Dormir", Dormir));
            else animations.Items.Add(Element("Vérifier", () => Faire(revue, 2, null)));
            animations.Items.Add(Element("Raté", () => Faire(rate, 1, null)));
            menu.Items.Add(animations);

            if (Perso.Tenues.Length > 1)
            {
                menuTenues = new MenuItem { Header = "Tenue" };
                for (int i = 0; i < Perso.Tenues.Length; i++)
                {
                    if (!TenueExiste(i)) continue;
                    int numero = i;
                    var choix = new MenuItem { Header = Perso.Tenues[i], IsCheckable = true, IsChecked = i == tenue, Tag = i };
                    choix.Click += (s, e) => { choix.IsChecked = true; ChangerTenue(numero); };
                    menuTenues.Items.Add(choix);
                }
                menu.Items.Add(menuTenues);
            }

            var taille = new MenuItem { Header = "Taille" };
            var tailles = new[] { "Petite", "Moyenne", "Grande", "Très grande" };
            var valeurs = new[] { 0.5, 0.75, 1.0, 1.5 };
            for (int i = 0; i < tailles.Length; i++)
            {
                double valeur = valeurs[i];
                var choix = new MenuItem { Header = tailles[i], IsCheckable = true, IsChecked = Math.Abs(echelle - valeur) < 0.01 };
                choix.Click += (s, e) =>
                {
                    foreach (MenuItem autre in taille.Items) autre.IsChecked = autre == choix;
                    echelle = valeur;
                    AppliquerEchelle();
                    Enregistrer();
                };
                taille.Items.Add(choix);
            }
            menu.Items.Add(taille);

            menu.Items.Add(Coche("Se balader de temps en temps", balade, v => { balade = v; Enregistrer(); }));
            if (Perso.Musique && AGeste(LigneDanse))
                menu.Items.Add(Coche("Danser quand il y a de la musique", dansePermise, v =>
                {
                    dansePermise = v;
                    if (oreille != null) oreille.Active = v;
                    if (!v) Musique(false, periode);
                    Enregistrer();
                }));
            if (AGeste(LigneBisou) || AGeste(LigneCoeur))
                menu.Items.Add(Coche("Envoyer des cœurs et des bisous", coeurs, v => { coeurs = v; Enregistrer(); }));
            menu.Items.Add(Coche("Toujours au premier plan", premierPlan, v => { premierPlan = v; Topmost = v; Enregistrer(); }));
            menu.Items.Add(Coche("Lancer au démarrage de Windows", LanceAuDemarrage(), DefinirDemarrage));
            menu.Items.Add(Element("Revenir dans le coin", () =>
            {
                if (mode == Mode.Saut) return;
                support = IntPtr.Zero;
                maison = PlaceParDefaut();
                ancre = maison;
                Placer();
                Enregistrer();
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Element("Quitter", Close));
            sprite.ContextMenu = menu;
        }

        static MenuItem Element(string titre, Action action)
        {
            var element = new MenuItem { Header = titre };
            element.Click += (s, e) => action();
            return element;
        }

        static MenuItem Coche(string titre, bool cochee, Action<bool> action)
        {
            var element = new MenuItem { Header = titre, IsCheckable = true, IsChecked = cochee };
            element.Click += (s, e) => action(element.IsChecked);
            return element;
        }

        void OuvrirMenu()
        {
            menu.PlacementTarget = pilule;
            menu.Placement = PlacementMode.Top;
            menu.IsOpen = true;
        }

        void MontrerPilule()
        {
            minuteurPilule.Stop();
            pilule.IsHitTestVisible = true;
            Fondu(pilule, 1);
        }

        void CacherPilule()
        {
            minuteurPilule.Stop();
            if (menu.IsOpen || sprite.IsMouseOver || pilule.IsMouseOver) return;
            pilule.IsHitTestVisible = false;
            Fondu(pilule, 0);
        }

        void Dire(string texte, double secondes)
        {
            texteBulle.Text = texte;
            Fondu(bulle, 1);
            minuteurBulle.Stop();
            if (secondes > 0)
            {
                minuteurBulle.Interval = TimeSpan.FromSeconds(secondes);
                minuteurBulle.Start();
            }
        }

        void CacherBulle()
        {
            minuteurBulle.Stop();
            Fondu(bulle, 0);
        }

        static void Fondu(UIElement element, double opacite)
        {
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(opacite, TimeSpan.FromMilliseconds(160)));
        }

        static DropShadowEffect Ombre()
        {
            return new DropShadowEffect { BlurRadius = 9, ShadowDepth = 1, Direction = 270, Opacity = 0.28, Color = Colors.Black };
        }

        static Brush Pinceau(byte r, byte v, byte b)
        {
            var pinceau = new SolidColorBrush(Color.FromRgb(r, v, b));
            pinceau.Freeze();
            return pinceau;
        }

        static DispatcherTimer Minuteur(double millisecondes, EventHandler tic)
        {
            var minuteur = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(millisecondes) };
            minuteur.Tick += tic;
            return minuteur;
        }

        // ------------------------------------------------------- place sur l'écran

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // fenêtre-outil : absente d'Alt+Tab
            poignee = new WindowInteropHelper(this).Handle;
            SetWindowLong(poignee, GWL_EXSTYLE, GetWindowLong(poignee, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        }

        static Point PlaceParDefaut()
        {
            Rect zone = SystemParameters.WorkArea;
            return new Point(zone.Right - DroiteDefaut, zone.Bottom - BasDefaut);
        }

        void AppliquerEchelle()
        {
            sprite.Width = CaseL * echelle;
            sprite.Height = CaseH * echelle;
            Width = Math.Max(CaseL * echelle, LargeurMin);
            Height = ZoneBulle + CaseH * echelle + ZonePilule;
            // la bulle descend dans le vide de la case, juste au-dessus de la tête
            bulle.Margin = new Thickness(0, 0, 0, -Math.Max(0, tetes[forme] * echelle - 8));
            Placer();
        }

        void Placer()
        {
            Left = ancre.X - Width / 2;
            Top = ancre.Y - (ZoneBulle + CaseH * echelle);
        }

        void Borner()
        {
            double gauche = SystemParameters.VirtualScreenLeft, haut = SystemParameters.VirtualScreenTop;
            ancre.X = Math.Max(gauche + 40, Math.Min(gauche + SystemParameters.VirtualScreenWidth - 40, ancre.X));
            ancre.Y = Math.Max(haut + 80, Math.Min(haut + SystemParameters.VirtualScreenHeight - 10, ancre.Y));
        }

        Point Curseur()
        {
            POINT p;
            GetCursorPos(out p);
            return VersUnites().Transform(new Point(p.X, p.Y));
        }

        // pixels de l'écran <-> unités WPF (différents dès que Windows agrandit l'affichage)
        Matrix VersUnites()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
        }

        Matrix VersPixels()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source != null ? source.CompositionTarget.TransformToDevice : Matrix.Identity;
        }

        // -------------------------------------------------------------- animation

        void Jouer(Animation animation, int nombreDeTours, Action aLaFin)   // 0 tour = en boucle
        {
            anim = animation;
            index = 0;
            tours = nombreDeTours;
            fin = aLaFin;
            Afficher();
        }

        void Afficher()
        {
            sprite.Source = images[anim.Ligne, anim.Colonnes[index]];
            horloge.Stop();
            horloge.Interval = TimeSpan.FromMilliseconds(anim.Durees[index]);
            horloge.Start();
            if (dessins != null && anim.Ligne >= LigneBisou) Effets();
        }

        void Avancer(object s, EventArgs e)
        {
            index++;
            if (index >= anim.Colonnes.Length)
            {
                index = 0;
                if (tours > 0 && --tours == 0)
                {
                    horloge.Stop();
                    Action suite = fin;
                    fin = null;
                    if (suite != null) suite(); else Reprendre();
                    return;
                }
            }
            Afficher();
        }

        // Retour à l'activité de fond : ce que fait Claude Code si on le sait, sinon le repos.
        void Reprendre()
        {
            if (agent == Agent.Travail) { mode = Mode.Agent; Jouer(travail, 0, null); }
            else if (agent == Agent.Attente) { mode = Mode.Agent; Jouer(attente, 0, null); }
            else if (musique) Phrase();
            else
            {
                mode = Mode.Repos;
                Jouer(respiration, 0, null);
                minuteurHasard.Stop();
                minuteurHasard.Interval = TimeSpan.FromSeconds(20 + hasard.Next(30));
                minuteurHasard.Start();
            }
        }

        void Faire(Animation animation, int nombreDeTours, string parole)
        {
            if (mode == Mode.Saut) return;                       // on ne l'interrompt pas en plein vol
            mode = Mode.Geste;
            minuteurHasard.Stop();
            Jouer(animation, nombreDeTours, Reprendre);
            if (parole != null) Dire(parole, 3);
        }

        void Cligner(object s, EventArgs e)
        {
            minuteurClin.Interval = TimeSpan.FromMilliseconds(2500 + hasard.Next(4000));
            if (mode != Mode.Glisse && mode != Mode.Saut) Placer();     // si un autre programme a déplacé la fenêtre, elle revient à sa place
            if (mode == Mode.Repos && anim == respiration)
                Jouer(clin, 1, () => Jouer(respiration, 0, null));
        }

        void GesteAuHasard()
        {
            double tirage = hasard.NextDouble();
            if (AGeste(LigneBisou) && hasard.Next(5) < 2)
            {
                int geste = coeurs ? hasard.Next(20) : 14 + hasard.Next(6);      // sans les cœurs : danse ou chant
                if (geste < 7) Bisou();
                else if (geste < 14) Coeur();
                else if (geste < 17) Danser();
                else Chanter();
            }
            else if (objets != null && hasard.Next(4) == 0) Bloc();
            else if (Perso.Plateformes && balade && tirage < 0.4)
            {
                if (support != IntPtr.Zero && hasard.Next(3) == 0) Descendre(); else SauterSurFenetre();
            }
            else if (Perso.Sieste && tirage < 0.25) Dormir();
            else if (balade && tirage < 0.6) Balade();
            else if (tirage < 0.78) Faire(salut, 2, null);
            else if (tirage < 0.92) SautSurPlace(null);
            else if (Perso.Sieste) Dormir();
            else Faire(revue, 2, null);
        }

        void Dormir()
        {
            Faire(sieste, 4, "Zzz…");
        }

        string Parole()
        {
            return AuHasard(Perso.Paroles);
        }

        string AuHasard(string[] phrases)
        {
            return phrases.Length == 0 ? null : phrases[hasard.Next(phrases.Length)];
        }

        // ------------------------------------------------------------------ marche

        void Balade()
        {
            if (mode == Mode.Saut) return;
            double gauche = SystemParameters.VirtualScreenLeft + Width / 2;
            double droite = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width / 2;
            double centre = maison.X;
            Bord bord = new Bord();
            bool perchee = support != IntPtr.Zero && LireBord(support, out bord);
            if (perchee) { gauche = bord.Gauche + 30; droite = bord.Droite - 30; centre = ancre.X; }   // elle reste sur sa fenêtre

            double but;
            if (!perchee && Math.Abs(ancre.X - maison.X) > 20 && hasard.NextDouble() < 0.6)
                but = maison.X;                                  // le plus souvent, elle rentre chez elle
            else
            {
                double distance = 80 + hasard.NextDouble() * 180;
                if (hasard.Next(2) == 0) distance = -distance;
                but = Math.Max(gauche, Math.Min(droite, centre + distance));
                if (Math.Abs(but - ancre.X) < 40)                // coincée contre un bord : partir de l'autre côté
                    but = Math.Max(gauche, Math.Min(droite, centre - distance));
            }
            if (Math.Abs(but - ancre.X) < 10) { Reprendre(); return; }
            Marcher(but, null);
        }

        void Marcher(double but, Action enArrivant)
        {
            mode = Mode.Marche;
            minuteurHasard.Stop();
            cible = but;
            apresMarche = enArrivant;
            bool course = enArrivant != null;
            if (cible > ancre.X) Jouer(course ? courseDroite : marcheDroite, 0, null);
            else Jouer(course ? courseGauche : marcheGauche, 0, null);
            dernierPas = DateTime.UtcNow;
            minuteurMarche.Start();
        }

        void Pas(object s, EventArgs e)
        {
            if (mode != Mode.Marche) { minuteurMarche.Stop(); apresMarche = null; return; }
            DateTime maintenant = DateTime.UtcNow;
            double vitesse = (apresMarche != null ? 300 : 70) * echelle;
            double pas = vitesse * Math.Min(0.1, (maintenant - dernierPas).TotalSeconds);
            dernierPas = maintenant;
            double avant = ancre.X;
            bool arrivee = Math.Abs(cible - ancre.X) <= pas;
            ancre.X = arrivee ? cible : ancre.X + (cible > ancre.X ? pas : -pas);
            if (support != IntPtr.Zero) supportX += ancre.X - avant;
            Placer();
            if (!arrivee) return;

            minuteurMarche.Stop();
            Action suite = apresMarche;
            apresMarche = null;
            if (suite != null) suite(); else Reprendre();
        }

        // ------------------------------------------------------------------ souris

        void Appui(object s, MouseButtonEventArgs e)
        {
            appuiCurseur = Curseur();
            appuiAncre = ancre;
            glisse = false;
            sprite.CaptureMouse();
        }

        void Deplacement(object s, MouseEventArgs e)
        {
            if (!sprite.IsMouseCaptured) return;
            Point curseur = Curseur();
            double dx = curseur.X - appuiCurseur.X, dy = curseur.Y - appuiCurseur.Y;
            if (!glisse)
            {
                if (Math.Abs(dx) + Math.Abs(dy) < 5) return;     // simple clic qui tremble
                glisse = true;
                mode = Mode.Glisse;
                minuteurHasard.Stop();
                minuteurSaut.Stop();
                support = IntPtr.Zero;
                sensGlisse = 0;
                dernierX = appuiCurseur.X;
            }
            ancre = new Point(appuiAncre.X + dx, appuiAncre.Y + dy);
            Borner();
            Placer();

            // elle court dans le sens où on la tire
            double vitesse = curseur.X - dernierX;
            if (sensGlisse == 0 || Math.Abs(vitesse) >= 3)
            {
                int sens = vitesse < 0 ? -1 : 1;
                if (sens != sensGlisse)
                {
                    sensGlisse = sens;
                    Jouer(sens > 0 ? marcheDroite : marcheGauche, 0, null);
                }
                dernierX = curseur.X;
            }
        }

        void Relache(object s, MouseButtonEventArgs e)
        {
            if (!sprite.IsMouseCaptured) return;
            sprite.ReleaseMouseCapture();
            if (!glisse) Clic();
            else if (Perso.Plateformes) Tomber();                // lâchée en l'air : elle retombe sur la fenêtre du dessous, ou au sol
            else
            {
                maison = ancre;
                Enregistrer();
                Reprendre();
            }
        }

        void Clic()
        {
            switch (Perso.Clic)
            {
                case "lien":
                    Faire(salut, 2, null);
                    OuvrirLien();
                    break;
                case "saut":
                    SautSurPlace(Parole());
                    break;
                case "parole":
                    if (++clics % 3 == 0) SautSurPlace(Parole()); else Faire(salut, 2, Parole());
                    break;
                case "bisou":                                    // bisou, cœur, salut, chacun son tour
                    switch (coeurs ? clics++ % 3 : 2)
                    {
                        case 0: Bisou(); break;
                        case 1: Coeur(); break;
                        default: Faire(salut, 2, Parole()); break;
                    }
                    break;
                default:
                    if (++clics % 3 == 0) SautSurPlace(null); else Faire(salut, 2, null);
                    break;
            }
        }

        // Le premier bouton de la pilule (et la première ligne du menu) : dépend du personnage.
        void ActionPrincipale()
        {
            switch (Perso.Role)
            {
                case "claude": OuvrirClaude(); break;
                case "lien": OuvrirLien(); break;
                case "plateforme": SauterSurFenetre(); break;
                case "bisou": Bisou(); break;
                default: Faire(salut, 2, Parole()); break;
            }
        }

        void Ouvrir(string adresse)
        {
            try { Process.Start(adresse); }
            catch (Exception) { Dire("Impossible d'ouvrir le lien", 3); }
        }

        // Si le personnage a une appli installée (raccourci du menu Démarrer), on ouvre l'appli plutôt
        // que le site, et on la ramène au premier plan quand elle tourne déjà.
        void OuvrirLien()
        {
            if (Perso.Appli.Length > 0)
            {
                IntPtr ouverte = FenetreAppli();
                if (ouverte != IntPtr.Zero)
                {
                    if (IsIconic(ouverte)) ShowWindow(ouverte, SW_RESTORE);
                    SetForegroundWindow(ouverte);
                    return;
                }
                string raccourci = RaccourciAppli();
                if (raccourci != null) { Ouvrir(raccourci); return; }
            }
            Ouvrir(Perso.Lien);
        }

        static IntPtr FenetreAppli()
        {
            IntPtr trouvee = IntPtr.Zero;
            EnumWindows((fenetre, parametre) =>
            {
                if (!IsWindowVisible(fenetre)) return true;
                var classe = new StringBuilder(64);
                var texte = new StringBuilder(300);
                GetClassName(fenetre, classe, classe.Capacity);
                GetWindowText(fenetre, texte, texte.Capacity);
                string titre = texte.ToString();
                // une appli web installée a sa propre fenêtre Chrome, dont le titre ne finit pas par le nom du navigateur
                if (classe.ToString() != "Chrome_WidgetWin_1" || titre.IndexOf(Perso.Appli, StringComparison.OrdinalIgnoreCase) < 0
                    || titre.EndsWith("Google Chrome") || titre.EndsWith("Edge"))
                    return true;
                trouvee = fenetre;
                return false;
            }, IntPtr.Zero);
            return trouvee;
        }

        static string RaccourciAppli()
        {
            foreach (Environment.SpecialFolder dossier in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
                try
                {
                    string[] trouves = Directory.GetFiles(Environment.GetFolderPath(dossier), Perso.Appli + ".lnk", SearchOption.AllDirectories);
                    if (trouves.Length > 0) return trouves[0];
                }
                catch (Exception) { }                            // dossier illisible : on essaie le suivant
            return null;
        }

        void OuvrirClaude()
        {
            // l'appli de bureau Claude si elle est installée (protocole claude://), sinon le site
            bool appli;
            using (RegistryKey cle = Registry.ClassesRoot.OpenSubKey("claude")) appli = cle != null;
            try { Process.Start(appli ? "claude://claude.ai/new" : "https://claude.ai/new"); }
            catch (Exception) { Dire("Impossible d'ouvrir Claude", 3); }
        }

        // ------------------------------------------- états envoyés par Claude Code

        void SurveillerEtat()
        {
            guetteur = new FileSystemWatcher(Dossier, Path.GetFileName(FichierEtat));
            guetteur.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            guetteur.Changed += EtatModifie;
            guetteur.Created += EtatModifie;
            guetteur.EnableRaisingEvents = true;
        }

        void EtatModifie(object s, FileSystemEventArgs e)
        {
            // appelé hors du fil de l'interface ; le fichier peut encore être tenu par celui qui l'écrit
            for (int essai = 0; essai < 6; essai++)
            {
                try
                {
                    string[] lignes = File.ReadAllLines(FichierEtat);
                    if (lignes.Length == 0) return;
                    string etat = lignes[0].Trim().ToLowerInvariant();
                    string message = lignes.Length > 1 ? lignes[1].Trim() : "";
                    Dispatcher.BeginInvoke(new Action(() => AppliquerEtat(etat, message)));
                    return;
                }
                catch (IOException) { Thread.Sleep(40); }
            }
        }

        void AppliquerEtat(string etat, string message)
        {
            if (mode == Mode.Saut) return;                       // en plein vol : tant pis pour cet état
            // le guetteur signale souvent deux fois la même écriture
            string empreinte = etat + "\n" + message;
            if (empreinte == dernierEtat && (DateTime.UtcNow - heureEtat).TotalMilliseconds < 500) return;
            dernierEtat = empreinte;
            heureEtat = DateTime.UtcNow;

            bool muet = message.Length == 0;
            minuteurAgent.Stop();
            switch (etat)
            {
                case "running": case "travail":
                    minuteurAgent.Start();
                    // un hook renvoie « running » après chaque outil : on ne recommence ni la bulle ni l'animation
                    if (agent == Agent.Travail && muet) return;
                    agent = Agent.Travail;
                    Dire(muet ? "Je m'en occupe…" : message, 4);
                    break;
                case "waiting": case "attente":
                    minuteurAgent.Start();
                    if (agent == Agent.Attente && muet) return;
                    agent = Agent.Attente;
                    Dire(muet ? "J'ai besoin de toi !" : message, 0);
                    break;
                case "review": case "fini":
                    agent = Agent.Aucun;
                    Dire(muet ? "C'est prêt !" : message, 6);
                    if (mode != Mode.Glisse) { mode = Mode.Geste; minuteurHasard.Stop(); Jouer(revue, 2, () => Faire(salut, 2, null)); }
                    return;
                case "failed": case "erreur":
                    agent = Agent.Aucun;
                    Dire(muet ? "Aïe, ça a coincé…" : message, 6);
                    if (mode != Mode.Glisse) Faire(rate, 1, null);
                    return;
                case "waving": case "salut":
                    if (mode != Mode.Glisse) Faire(salut, 2, muet ? null : message);
                    return;
                case "jumping": case "saut":
                    if (mode != Mode.Glisse) SautSurPlace(muet ? null : message);
                    return;
                case "walking": case "balade":
                    if (mode != Mode.Glisse) Balade();
                    return;
                case "platform": case "plateforme":
                    if (mode != Mode.Glisse) SauterSurFenetre();
                    return;
                case "down": case "descendre":
                    if (mode != Mode.Glisse) Descendre();
                    return;
                case "sleep": case "dodo":
                    if (mode != Mode.Glisse) Dormir();
                    return;
                case "block": case "bloc":
                    if (mode != Mode.Glisse) Bloc();
                    return;
                case "small": case "petit":
                    if (mode != Mode.Glisse && forme != 0) Transformer(0);
                    return;
                case "kiss": case "bisou":
                    if (mode != Mode.Glisse) Bisou();
                    return;
                case "heart": case "coeur":
                    if (mode != Mode.Glisse) Coeur();
                    return;
                case "dance": case "danse":
                    if (mode != Mode.Glisse) Danser();
                    return;
                case "sing": case "chant":
                    if (mode != Mode.Glisse) Chanter();
                    return;
                case "outfit": case "tenue":                     // --etat tenue [numéro ou nom] ; sans rien : la suivante
                    if (mode == Mode.Glisse || Perso.Tenues.Length < 2) return;
                    int voulue = Array.FindIndex(Perso.Tenues, t => string.Equals(t, message, StringComparison.OrdinalIgnoreCase));
                    if (voulue < 0 && !int.TryParse(message, out voulue)) voulue = (tenue + 1) % Perso.Tenues.Length;
                    ChangerTenue(voulue);
                    return;
                default:                                         // idle, repos, ou état inconnu
                    agent = Agent.Aucun;
                    if (muet) CacherBulle(); else Dire(message, 4);
                    break;
            }
            if (mode != Mode.Glisse) Reprendre();
        }

        // ---------------------------------------------------------------- réglages

        void LireReglages()
        {
            double droite = DroiteDefaut, bas = BasDefaut;
            if (File.Exists(FichierReglages))
                foreach (string ligne in File.ReadAllLines(FichierReglages))
                {
                    string[] morceaux = ligne.Split('=');
                    double nombre;
                    if (morceaux.Length != 2 || !double.TryParse(morceaux[1], NumberStyles.Float, CultureInfo.InvariantCulture, out nombre)) continue;
                    switch (morceaux[0])
                    {
                        case "echelle": echelle = Math.Max(0.3, Math.Min(3, nombre)); break;
                        case "droite": droite = nombre; break;
                        case "bas": bas = nombre; break;
                        case "balade": balade = nombre != 0; break;
                        case "premierplan": premierPlan = nombre != 0; break;
                        case "pieces": pieces = (int)nombre; break;
                        case "musique": dansePermise = nombre != 0; break;
                        case "coeurs": coeurs = nombre != 0; break;
                        case "tenue": tenue = Math.Max(0, (int)nombre); break;
                    }
                }
            if (Perso.Plateformes) bas = BasDefaut;               // celle qui saute sur les fenêtres repart toujours du sol
            // la place est retenue par rapport au coin bas-droit de l'écran, là où elle vit d'habitude
            Rect zone = SystemParameters.WorkArea;
            ancre = new Point(zone.Right - droite, zone.Bottom - bas);
            Borner();
            maison = ancre;
        }

        void Enregistrer()
        {
            Rect zone = SystemParameters.WorkArea;
            CultureInfo c = CultureInfo.InvariantCulture;
            File.WriteAllLines(FichierReglages, new[]
            {
                "echelle=" + echelle.ToString(c),
                "droite=" + (zone.Right - maison.X).ToString("0.#", c),
                "bas=" + (zone.Bottom - maison.Y).ToString("0.#", c),
                "balade=" + (balade ? "1" : "0"),
                "premierplan=" + (premierPlan ? "1" : "0"),
                "pieces=" + pieces,
                "musique=" + (dansePermise ? "1" : "0"),
                "coeurs=" + (coeurs ? "1" : "0"),
                "tenue=" + tenue
            });
        }

        static bool LanceAuDemarrage()
        {
            using (RegistryKey cle = Registry.CurrentUser.OpenSubKey(CleDemarrage))
                return cle != null && cle.GetValue(NomDemarrage) != null;
        }

        static void DefinirDemarrage(bool actif)
        {
            using (RegistryKey cle = Registry.CurrentUser.OpenSubKey(CleDemarrage, true))
            {
                if (cle == null) return;
                if (actif) cle.SetValue(NomDemarrage, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                else cle.DeleteValue(NomDemarrage, false);
            }
        }

        // ------------------------------------------------- fenêtres-plateformes
        // Le haut d'une fenêtre visible sert de sol : la mascotte y saute, s'y promène, suit la fenêtre
        // quand on la déplace, et retombe si la fenêtre disparaît ou passe derrière une autre.

        struct Bord
        {
            public IntPtr Fenetre;
            public double Gauche, Droite, Haut;
        }

        double Pieds { get { return (CaseH - PiedsCase) * echelle; } }      // de la plante des pieds à l'ancre

        double Sol { get { return SystemParameters.WorkArea.Bottom - BasDefaut; } }

        bool LireBord(IntPtr fenetre, out Bord bord)
        {
            bord = new Bord();
            RECT r;
            int voile;
            if (!IsWindow(fenetre) || !IsWindowVisible(fenetre) || IsIconic(fenetre)) return false;
            if (DwmGetWindowAttribute(fenetre, DWMWA_CLOAKED, out voile, 4) == 0 && voile != 0) return false;   // sur un autre bureau virtuel
            if (DwmGetWindowAttribute(fenetre, DWMWA_EXTENDED_FRAME_BOUNDS, out r, 16) != 0) return false;
            Matrix m = VersUnites();
            Point hautGauche = m.Transform(new Point(r.Left, r.Top)), basDroite = m.Transform(new Point(r.Right, r.Bottom));
            if (basDroite.X - hautGauche.X < 220 || basDroite.Y - hautGauche.Y < 80) return false;
            if (hautGauche.Y < SystemParameters.VirtualScreenTop + (PiedsCase - tetes[forme] + 12) * echelle) return false;   // pas la place de se tenir dessus
            bord.Fenetre = fenetre;
            bord.Gauche = hautGauche.X;
            bord.Droite = basDroite.X;
            bord.Haut = hautGauche.Y;
            return true;
        }

        List<Bord> Bords()
        {
            var bords = new List<Bord>();
            uint moi = (uint)Process.GetCurrentProcess().Id;
            EnumWindows((fenetre, parametre) =>
            {
                uint processus;
                Bord bord;
                GetWindowThreadProcessId(fenetre, out processus);
                int style = GetWindowLong(fenetre, GWL_EXSTYLE);
                // ni nos propres fenêtres, ni les calques et fenêtres-outils (dont les autres mascottes)
                if (processus != moi && (style & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) == 0
                    && GetWindowTextLength(fenetre) > 0 && LireBord(fenetre, out bord))
                    bords.Add(bord);
                return true;
            }, IntPtr.Zero);
            return bords;
        }

        // Le bord est-il vraiment visible à cet endroit, ou caché par une fenêtre passée devant ?
        bool BordLibre(Bord bord, double x)
        {
            Point pixel = VersPixels().Transform(new Point(x, bord.Haut + 3));
            POINT p;
            p.X = (int)pixel.X;
            p.Y = (int)pixel.Y;
            IntPtr dessus = GetAncestor(WindowFromPoint(p), GA_ROOT);
            if (dessus != bord.Fenetre && dessus != poignee) Trace("  en " + p.X + "," + p.Y + " : fenetre " + dessus + " devant " + bord.Fenetre);
            return dessus == bord.Fenetre || dessus == poignee;
        }

        bool ChoisirBord(out Point arrivee, out IntPtr fenetre)
        {
            arrivee = new Point();
            fenetre = IntPtr.Zero;
            double meilleur = double.MaxValue;
            foreach (Bord bord in Bords())
            {
                Point point;
                if (bord.Fenetre == support || !PointSur(bord.Fenetre, out point)) continue;
                double distance = (Math.Abs(point.X - ancre.X) + 100) * (0.5 + hasard.NextDouble());   // la plus proche, à peu près
                if (distance < meilleur)
                {
                    meilleur = distance;
                    arrivee = point;
                    fenetre = bord.Fenetre;
                }
            }
            Trace("choix : fenetre " + fenetre + " en " + arrivee);
            return fenetre != IntPtr.Zero;
        }

        // Un point libre où se poser sur le haut de cette fenêtre : le plus proche d'elle, sinon au hasard.
        bool PointSur(IntPtr fenetre, out Point arrivee)
        {
            arrivee = new Point();
            Bord bord;
            if (!LireBord(fenetre, out bord) || bord.Haut + Pieds > Sol - 60) return false;
            double gauche = bord.Gauche + 40, droite = bord.Droite - 40;
            for (int essai = 0; essai < 5; essai++)
            {
                double x = essai == 0 ? Math.Max(gauche, Math.Min(droite, ancre.X)) : gauche + hasard.NextDouble() * (droite - gauche);
                if (!BordLibre(bord, x)) continue;
                arrivee = new Point(x, bord.Haut + Pieds);
                return true;
            }
            return false;
        }

        void SauterSurFenetre()
        {
            if (mode == Mode.Saut || mode == Mode.Glisse) return;
            Point arrivee;
            IntPtr fenetre;
            if (!ChoisirBord(out arrivee, out fenetre))
            {
                if (support != IntPtr.Zero) Descendre(); else SautSurPlace(null);
                return;
            }
            double ecart = arrivee.X - ancre.X;
            if (Math.Abs(ecart) <= 1200) Sauter(arrivee, fenetre);
            else if (support != IntPtr.Zero) Descendre();
            else
            {
                // trop loin pour un seul bond : elle court d'abord vers la fenêtre, puis saute dessus si elle est toujours là
                Marcher(arrivee.X - Math.Sign(ecart) * 500, () =>
                {
                    Point point;
                    if (PointSur(fenetre, out point)) Sauter(point, fenetre); else Reprendre();
                });
            }
        }

        void Descendre()
        {
            if (mode == Mode.Saut || mode == Mode.Glisse) return;
            if (support == IntPtr.Zero) { SautSurPlace(null); return; }
            double gauche = SystemParameters.VirtualScreenLeft + Width / 2;
            double droite = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width / 2;
            double x = ancre.X + (hasard.Next(2) == 0 ? -1 : 1) * (60 + hasard.NextDouble() * 160);
            Sauter(new Point(Math.Max(gauche, Math.Min(droite, x)), Sol), IntPtr.Zero);
        }

        void Sauter(Point arrivee, IntPtr fenetre)
        {
            double montee = Math.Max(0, ancre.Y - arrivee.Y);
            // pour se poser sur un bord il faut arriver par le dessus : le sommet dépasse nettement l'arrivée
            Envol(arrivee, fenetre, false, montee / 4 + 90,
                Math.Max(0.55, Math.Min(1.25, 0.5 + (Math.Abs(arrivee.X - ancre.X) + Math.Abs(arrivee.Y - ancre.Y)) / 1600)));
            string cri = Parole();
            if (cri != null && hasard.Next(2) == 0) Dire(cri, 2);
        }

        // Elle n'a plus rien sous les pieds : chute jusqu'au premier bord libre en dessous, sinon jusqu'au sol.
        void Tomber()
        {
            AnnulerBloc();
            Point arrivee = new Point(ancre.X, Sol);
            IntPtr fenetre = IntPtr.Zero;
            foreach (Bord bord in Bords())
            {
                double y = bord.Haut + Pieds;
                if (bord.Fenetre != support && y > ancre.Y + 4 && y < arrivee.Y && y < Sol - 60
                    && ancre.X > bord.Gauche + 6 && ancre.X < bord.Droite - 6 && BordLibre(bord, ancre.X))
                {
                    arrivee.Y = y;
                    fenetre = bord.Fenetre;
                }
            }
            Envol(arrivee, fenetre, true, 0, Math.Max(0.12, Math.Min(0.75, Math.Sqrt(Math.Abs(arrivee.Y - ancre.Y)) / 38)));
        }

        // ------------------------------------------------------------ le bloc « ? »
        // Un bloc apparaît au-dessus d'elle ; elle saute, le frappe de la tête, et il en sort une pièce,
        // un champignon (elle grandit) ou une fleur (forme suivante). Le pouvoir s'en va au bout d'un moment.

        const int TuileBloc = 0, TuileVide = 1, TuileChampignon = 2, TuilePiece = 3, TuileFleur = 4;
        const double DureePouvoir = 150000;      // ms avant de redevenir petite

        Objet bloc, butin;
        int etapeBloc, typeButin, sensButin, formeVisee, formeAvant, pieces;
        DateTime tempsBloc, ticBloc;
        Point coinBloc, posButin;        // coin haut-gauche des deux petites fenêtres
        double chuteButin, solButin;
        Action finVol;                   // à l'atterrissage, à la place de la réception habituelle

        double Tuile { get { return 96 * echelle; } }

        void SautSurPlace(string parole)
        {
            if (!Perso.Plateformes) { Faire(saut, 1, parole); return; }
            if (mode == Mode.Saut || mode == Mode.Glisse) return;
            // un vrai bond, par déplacement de la fenêtre : le grand Mario n'a pas la place de sauter dans sa case
            Envol(ancre, support, false, 1.3 * Tuile, 0.55);
            if (parole != null) Dire(parole, 2);
        }

        void Etape(int numero)
        {
            etapeBloc = numero;
            tempsBloc = ticBloc = DateTime.UtcNow;
        }

        void Bloc()
        {
            if (objets == null || etapeBloc != 0 || mode == Mode.Saut || mode == Mode.Glisse) return;
            double pieds = ancre.Y - Pieds;
            double taille = (PiedsCase - tetes[forme]) * echelle;
            double haut = pieds - taille - 2.1 * Tuile;          // le bloc flotte un peu plus d'une tuile au-dessus de sa tête
            if (haut < SystemParameters.VirtualScreenTop + 4) { SautSurPlace(null); return; }

            mode = Mode.Geste;
            minuteurHasard.Stop();
            Jouer(respiration, 0, null);
            if (bloc == null) { bloc = new Objet(); butin = new Objet(); }
            coinBloc = new Point(ancre.X - Tuile / 2, haut);
            bloc.Montrer(objets[TuileBloc], coinBloc, Tuile);
            Etape(1);
            minuteurBloc.Start();
        }

        void AnimerBloc(object s, EventArgs e)
        {
            if (mode == Mode.Glisse) { AnnulerBloc(); return; }
            DateTime maintenant = DateTime.UtcNow;
            double t = (maintenant - tempsBloc).TotalSeconds;
            double dt = Math.Min(0.05, (maintenant - ticBloc).TotalSeconds);
            ticBloc = maintenant;
            switch (etapeBloc)
            {
                case 1:                                          // le bloc vient d'apparaître : elle prend son élan
                    if (t < 0.45) return;
                    finVol = () => Jouer(respiration, 0, null);  // au sol, elle attend la suite sans reprendre sa vie
                    Envol(ancre, support, false, 1.1 * Tuile + 4, 0.5);
                    Etape(2);
                    break;

                case 2:                                          // sommet du saut : coup de tête
                    if (t < 0.25) return;
                    bloc.Montrer(objets[TuileVide], coinBloc, Tuile);
                    ChoisirButin();
                    Etape(3);
                    break;

                case 3:                                          // le bloc tressaute, le butin en sort
                    bloc.Placer(coinBloc.X, coinBloc.Y - 0.22 * Tuile * Math.Sin(Math.Min(1, t / 0.18) * Math.PI));
                    if (typeButin == TuilePiece)
                    {
                        double k = t / 0.55;                     // la pièce jaillit et retombe
                        butin.Montrer(objets[TuilePiece], new Point(coinBloc.X, coinBloc.Y - Tuile - 6 * Tuile * k * (1 - k)), Tuile);
                        if (k < 1) return;
                        butin.Hide();
                        pieces++;
                        Enregistrer();
                        Dire("Pièce ! × " + pieces, 2);
                        FinBloc();
                    }
                    else
                    {
                        double k = Math.Min(1, t / 0.6);         // champignon ou fleur : il monte hors du bloc
                        posButin = new Point(coinBloc.X, coinBloc.Y - Tuile * k);
                        butin.Montrer(objets[typeButin], posButin, Tuile);
                        if (k < 1) return;
                        chuteButin = 0;
                        Etape(4);
                    }
                    break;

                case 4:                                          // il glisse hors du bloc puis tombe au sol
                    posButin.X += sensButin * 1.4 * Tuile * dt;
                    if (Math.Abs(posButin.X - coinBloc.X) >= Tuile)
                    {
                        chuteButin += 27 * Tuile * dt;
                        posButin.Y += chuteButin * dt;
                    }
                    if (posButin.Y >= solButin)
                    {
                        posButin.Y = solButin;
                        butin.Placer(posButin.X, posButin.Y);
                        Etape(5);
                        if (mode != Mode.Saut) Marcher(posButin.X + Tuile / 2, PrendreButin);
                        return;
                    }
                    butin.Placer(posButin.X, posButin.Y);
                    break;

                case 5:                                          // elle court le ramasser (voir PrendreButin)
                    if (t > 10) AnnulerBloc();
                    break;

                case 6:                                          // le bloc vide reste un instant, puis s'efface
                    if (t < 1.5) return;
                    if (bloc != null) bloc.Hide();
                    etapeBloc = 0;
                    minuteurBloc.Stop();
                    break;

                case 7:                                          // transformation : elle clignote entre ses deux formes
                    int clignotement = (int)(t / 0.09);
                    ChangerForme(clignotement >= 7 || clignotement % 2 == 0 ? formeVisee : formeAvant);
                    if (clignotement < 7) return;
                    Etape(6);
                    Reprendre();
                    break;
            }
        }

        // Champignon ou fleur seulement s'il y a la place de le laisser retomber à côté d'elle ; sinon une pièce.
        void ChoisirButin()
        {
            typeButin = TuilePiece;
            double tirage = hasard.NextDouble();
            if (formes.Count > 1 && forme == 0 && tirage < 0.5) typeButin = TuileChampignon;
            else if (formes.Count > 2 && forme == 1 && tirage < 0.4) typeButin = TuileFleur;
            if (typeButin == TuilePiece) return;

            double gauche = SystemParameters.VirtualScreenLeft + Tuile, droite = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Tuile;
            Bord bord;
            if (supportVise != IntPtr.Zero && LireBord(supportVise, out bord)) { gauche = bord.Gauche + Tuile / 2; droite = bord.Droite - Tuile / 2; }
            double portee = 2.4 * Tuile;                         // où il touche le sol, par rapport à elle
            if (ancre.X + portee <= droite) sensButin = 1;
            else if (ancre.X - portee >= gauche) sensButin = -1;
            else { typeButin = TuilePiece; return; }
            formeVisee = typeButin == TuileChampignon ? 1 : 2;
            solButin = arriveeSaut.Y - Pieds - Tuile;            // posé sur le même sol qu'elle
        }

        void PrendreButin()
        {
            butin.Hide();
            minuteurPouvoir.Stop();
            minuteurPouvoir.Interval = TimeSpan.FromMilliseconds(DureePouvoir);
            minuteurPouvoir.Start();
            Transformer(formeVisee);
        }

        void Transformer(int cible)
        {
            if (cible >= formes.Count || mode == Mode.Saut) return;
            mode = Mode.Geste;
            minuteurHasard.Stop();
            horloge.Stop();
            fin = null;
            formeAvant = forme;
            formeVisee = cible;
            Etape(7);
            minuteurBloc.Start();
        }

        void ChangerForme(int nouvelle)
        {
            if (nouvelle == forme) return;
            forme = nouvelle;
            images = formes[forme];
            sprite.Source = images[0, 0];
            AppliquerEchelle();                                  // la bulle suit la nouvelle hauteur de tête
        }

        void FinDuPouvoir(object s, EventArgs e)
        {
            if (forme == 0) { minuteurPouvoir.Stop(); return; }
            if (mode != Mode.Repos || etapeBloc != 0)
            {
                minuteurPouvoir.Interval = TimeSpan.FromSeconds(5);     // occupée : on réessaie un peu plus tard
                return;
            }
            minuteurPouvoir.Stop();
            Dire("Mamma mia !", 2);
            Transformer(0);
        }

        void FinBloc()
        {
            Etape(6);
            if (mode == Mode.Saut) finVol = null;                // encore en l'air : atterrissage normal
            else Reprendre();
        }

        void AnnulerBloc()
        {
            if (etapeBloc == 0) return;
            etapeBloc = 0;
            minuteurBloc.Stop();
            finVol = null;
            if (bloc != null) { bloc.Hide(); butin.Hide(); }
            if (mode == Mode.Geste) Reprendre();                 // elle attendait la suite : elle reprend sa vie
        }

        // Petite fenêtre transparente, insensible aux clics : le bloc, la pièce, le champignon.
        sealed class Objet : Window
        {
            readonly Image image = new Image { Stretch = Stretch.Fill };

            public Objet()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ResizeMode = ResizeMode.NoResize;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                WindowStartupLocation = WindowStartupLocation.Manual;
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Content = image;
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                IntPtr poignee = new WindowInteropHelper(this).Handle;
                SetWindowLong(poignee, GWL_EXSTYLE, GetWindowLong(poignee, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            }

            public IntPtr Dessous;       // fenêtre à laisser devant lui (la mascotte, pour que sa bulle reste lisible)

            public void Montrer(BitmapSource source, Point coin, double cote)
            {
                image.Source = source;
                Width = Height = cote;
                Placer(coin.X, coin.Y);
                if (IsVisible) return;
                Show();
                SetWindowPos(new WindowInteropHelper(this).Handle, Dessous != IntPtr.Zero ? Dessous : HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            public void Placer(double x, double y)
            {
                Left = x;
                Top = y;
            }
        }

        // Journal de diagnostic des sauts : actif seulement si un fichier trace.on existe dans le dossier de réglages.
        static readonly bool traceActive = File.Exists(Path.Combine(Dossier, "trace.on"));

        static void Trace(string texte)
        {
            if (!traceActive) return;
            try { File.AppendAllText(Path.Combine(Dossier, "trace.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + texte + "\r\n"); }
            catch (IOException) { }
        }

        void Envol(Point arrivee, IntPtr fenetre, bool enChute, double hauteur, double duree)
        {
            Trace((enChute ? "chute" : "saut") + " de " + ancre + " vers " + arrivee + " fenetre " + fenetre + " en " + duree.ToString("0.00") + " s");
            mode = Mode.Saut;
            minuteurHasard.Stop();
            horloge.Stop();
            fin = null;
            departSaut = ancre;
            arriveeSaut = arrivee;
            supportVise = fenetre;
            support = IntPtr.Zero;
            chute = enChute;
            hauteurSaut = hauteur;
            dureeSaut = duree;
            debutSaut = DateTime.UtcNow;
            BitmapSource air = images[9, arrivee.X >= ancre.X ? 0 : 1];
            sprite.Source = air ?? images[4, 2];                 // sans dixième ligne : le sommet du saut sur place
            minuteurSaut.Start();
        }

        void Vol(object s, EventArgs e)
        {
            if (mode != Mode.Saut) { minuteurSaut.Stop(); return; }
            double t = (DateTime.UtcNow - debutSaut).TotalSeconds / dureeSaut;
            if (t < 1)
            {
                double y = departSaut.Y + (arriveeSaut.Y - departSaut.Y) * (chute ? t * t : t) - 4 * hauteurSaut * t * (1 - t);
                ancre = new Point(departSaut.X + (arriveeSaut.X - departSaut.X) * t, y);
                Placer();
                return;
            }

            minuteurSaut.Stop();
            ancre = arriveeSaut;
            Placer();
            support = supportVise;
            Bord bord;
            if (support == IntPtr.Zero)
            {
                maison = ancre;
                Enregistrer();
            }
            else if (LireBord(support, out bord)) supportX = ancre.X - bord.Gauche;
            else
            {
                Tomber();                                        // la fenêtre a disparu pendant le saut
                return;
            }
            mode = Mode.Geste;
            Action suite = finVol;
            finVol = null;
            if (suite != null) suite(); else Jouer(reception, 1, Reprendre);
        }

        void SuivreSupport(object s, EventArgs e)
        {
            if (support == IntPtr.Zero || mode == Mode.Saut || mode == Mode.Glisse) return;
            Bord bord;
            if (!LireBord(support, out bord)) { Trace("support disparu"); Tomber(); return; }
            double x = bord.Gauche + supportX;
            if (x < bord.Gauche + 6 || x > bord.Droite - 6) { Trace("au bout du bord"); Tomber(); return; }
            if (!BordLibre(bord, x)) { Trace("bord masque en " + x.ToString("0") + "," + bord.Haut.ToString("0")); Tomber(); return; }
            double y = bord.Haut + Pieds;
            if (Math.Abs(x - ancre.X) > 0.5 || Math.Abs(y - ancre.Y) > 0.5)
            {
                ancre = new Point(x, y);                         // la fenêtre a bougé : elle voyage avec
                Placer();
            }
        }

        // ---------------------------------------------------------- danse et chant
        // Quand l'oreille entend de la musique, la mascotte danse sur place, une image par demi-temps,
        // et prend le micro de temps en temps. Elle envoie aussi des bisous (des cœurs qui filent jusqu'au
        // curseur) et fait des cœurs avec les mains, musique ou pas.

        // Ligne de danse ou de chant (8 images) au tempo donné. En musique, ce sont les demi-temps de
        // l'oreille qui la font avancer ; l'horloge, un peu plus lente, ne sert que s'ils tardent.
        Animation Rythme(int ligne, double unTemps)
        {
            int demi = (int)Math.Round(unTemps * 500);
            var durees = new int[Colonnes];
            var colonnes = new int[Colonnes];
            for (int i = 0; i < Colonnes; i++) { colonnes[i] = i; durees[i] = musique ? demi * 3 / 2 : demi; }
            return new Animation(ligne, colonnes, durees);
        }

        void Musique(bool active, double unTemps)
        {
            periode = unTemps;
            bool avant = musique;
            musique = active && dansePermise;
            if (musique == avant) return;
            if (musique)
            {
                // elle arrête de flâner pour danser ; un geste en cours se termine d'abord
                if (mode == Mode.Repos || (mode == Mode.Marche && apresMarche == null))
                {
                    Dire(AuHasard(Perso.Danses), 3);
                    Phrase();
                }
            }
            else if (mode == Mode.Danse) Reprendre();
        }

        void DemiTemps(bool fort)
        {
            if (mode != Mode.Danse || (anim.Ligne != LigneDanse && anim.Ligne != LigneChant)) return;
            Avancer(null, null);
            // les images paires tombent sur les temps
            if (fort && index % 2 == 1 && mode == Mode.Danse && (anim.Ligne == LigneDanse || anim.Ligne == LigneChant)) Avancer(null, null);
        }

        // Un morceau de chorégraphie (8 temps), puis le suivant : surtout de la danse, du chant
        // une fois sur trois, et de loin en loin un cœur ou un bisou.
        void Phrase()
        {
            if (!musique) { Reprendre(); return; }
            mode = Mode.Danse;
            minuteurHasard.Stop();
            phrase++;
            if (coeurs && phrase % 11 == 0 && AGeste(LigneBisou)) { Jouer(bisou, 1, Phrase); Dire(AuHasard(Perso.Bisous), 2.5); }
            else if (coeurs && phrase % 7 == 0 && AGeste(LigneCoeur)) { Jouer(coeur, 1, Phrase); Dire(AuHasard(Perso.Coeurs), 2.5); }
            else if (phrase % 3 == 0 && AGeste(LigneChant))
            {
                Jouer(Rythme(LigneChant, periode), 2, Phrase);
                Dire(AuHasard(Perso.Chansons), Math.Max(2.5, periode * 6));
            }
            else Jouer(Rythme(LigneDanse, periode), 2, Phrase);
        }

        void Bisou()
        {
            Faire(AGeste(LigneBisou) ? bisou : salut, AGeste(LigneBisou) ? 1 : 2, AuHasard(Perso.Bisous));
        }

        void Coeur()
        {
            Faire(AGeste(LigneCoeur) ? coeur : salut, AGeste(LigneCoeur) ? 1 : 2, AuHasard(Perso.Coeurs));
        }

        // Sans musique, on danse et on chante à 120 battements par minute.
        void Danser()
        {
            if (AGeste(LigneDanse)) Faire(Rythme(LigneDanse, musique ? periode : 0.5), 3, AuHasard(Perso.Danses));
        }

        void Chanter()
        {
            if (AGeste(LigneChant)) Faire(Rythme(LigneChant, musique ? periode : 0.5), 3, AuHasard(Perso.Chansons));
        }

        // Les dessins qui accompagnent certaines images des gestes.
        void Effets()
        {
            switch (anim.Ligne)
            {
                case LigneBisou: if (index == 2) EnvoyerBisou(); break;
                case LigneCoeur: if (index == 0) PetitsCoeurs(); else if (index == 3) GrandCoeur(); break;
                case LigneChant: if (index % 4 == 0) Note(); break;
                case LigneDanse: if (index == 0 && hasard.Next(2) == 0) Note(); break;
            }
        }

        // Repères sur l'écran, en unités WPF : le haut de la tête et la bouche (pose de repos).
        double HautTete { get { return ancre.Y - (CaseH - tetes[forme]) * echelle; } }

        Point Bouche()
        {
            double tete = HautTete, pieds = ancre.Y - Pieds;
            return new Point(ancre.X, tete + (pieds - tete) * 0.3);
        }

        double K { get { return echelle / 0.75; } }         // les dessins suivent la taille de la mascotte

        const int DessinCoeurRose = 0, DessinCoeurViolet = 1, DessinCoeurRouge = 2, DessinNote = 3, DessinDoubleNote = 4;

        int CouleurCoeur()
        {
            int tirage = hasard.Next(10);
            return tirage < 5 ? DessinCoeurRose : tirage < 8 ? DessinCoeurViolet : DessinCoeurRouge;
        }

        void EnvoyerBisou()
        {
            Point depart = Bouche();
            Point curseur = Curseur();
            double dx = curseur.X - depart.X, dy = curseur.Y - depart.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int sens = Math.Abs(dx) > 30 ? Math.Sign(dx) : (hasard.Next(2) == 0 ? -1 : 1);
            for (int i = 0; i < 3; i++)
            {
                double ecart = (i - 1) * 26 * K;
                if (distance > 170 * K)
                {
                    // trois cœurs filent jusqu'au curseur, en arc, et y éclatent
                    var arrivee = new Point(curseur.X + ecart * 0.6, curseur.Y + Math.Abs(ecart) * 0.3);
                    var controle = new Point((depart.X + arrivee.X) / 2 + ecart, Math.Min(depart.Y, arrivee.Y) - (80 + hasard.Next(60)) * K);
                    Lancer(CouleurCoeur(), depart, controle, arrivee, 0.8 + Math.Min(distance, 1500) / 1500 + i * 0.05,
                        16 * K, 30 * K, i * 0.14, true, 0);
                }
                else
                {
                    // le curseur est sur elle (on vient de cliquer) : les cœurs viennent vers l'écran en grossissant
                    var arrivee = new Point(depart.X + sens * 50 * K + ecart * 2.2, depart.Y - (110 + hasard.Next(40)) * K);
                    var controle = new Point(depart.X + sens * 30 * K + ecart, depart.Y - 40 * K);
                    Lancer(CouleurCoeur(), depart, controle, arrivee, 1.1, 14 * K, 46 * K, i * 0.14, false, 0);
                }
            }
        }

        // cœur avec les doigts : deux petits cœurs montent à côté du visage
        void PetitsCoeurs()
        {
            double tete = HautTete;
            for (int i = 0; i < 2; i++)
            {
                double x = ancre.X + (i == 0 ? -1 : 1) * (8 + hasard.Next(22)) * K;
                var depart = new Point(x, tete + 22 * K);
                Lancer(CouleurCoeur(), depart, new Point(x + (hasard.NextDouble() - 0.5) * 50 * K, tete - 25 * K),
                    new Point(x + (hasard.NextDouble() - 0.5) * 80 * K, tete - 85 * K), 1.3, 12 * K, 20 * K, i * 0.3, false, 4 * K);
            }
        }

        // grand cœur avec les bras : un gros cœur s'élève au-dessus de la tête, deux petits l'accompagnent
        void GrandCoeur()
        {
            double tete = HautTete;
            var depart = new Point(ancre.X, tete - 4 * K);
            Lancer(hasard.Next(2) == 0 ? DessinCoeurRose : DessinCoeurViolet, depart, new Point(ancre.X, tete - 40 * K),
                new Point(ancre.X, tete - 95 * K), 1.6, 26 * K, 46 * K, 0, false, 0);
            for (int i = 0; i < 2; i++)
            {
                int cote = i == 0 ? -1 : 1;
                Lancer(CouleurCoeur(), new Point(ancre.X + cote * 30 * K, tete + 10 * K), new Point(ancre.X + cote * 55 * K, tete - 20 * K),
                    new Point(ancre.X + cote * 70 * K, tete - 70 * K), 1.3, 12 * K, 18 * K, 0.15 + i * 0.1, false, 3 * K);
            }
        }

        // une note de musique s'échappe en ondulant
        void Note()
        {
            Point bouche = Bouche();
            int sens = hasard.Next(2) == 0 ? -1 : 1;
            var depart = new Point(bouche.X + sens * 26 * K, bouche.Y - 6 * K);
            Lancer(hasard.Next(2) == 0 ? DessinNote : DessinDoubleNote, depart, new Point(depart.X + sens * 30 * K, depart.Y - 40 * K),
                new Point(depart.X + sens * 48 * K, depart.Y - 105 * K), 1.6, 16 * K, 22 * K, 0, false, 7 * K);
        }

        // Chaque dessin vit dans sa petite fenêtre (comme le bloc « ? ») : il peut sortir de la case de la mascotte.
        sealed class Particule
        {
            public readonly Objet Fenetre = new Objet();
            public BitmapSource Image;
            public Point Depart, Controle, Arrivee;          // courbe de Bézier que suit son centre
            public double Duree, Taille0, Taille1, Retard, Balancement;
            public DateTime Debut;
            public bool Eclate, Libre = true;
        }

        readonly List<Particule> particules = new List<Particule>();

        void Lancer(int dessin, Point depart, Point controle, Point arrivee, double duree, double taille0, double taille1,
            double retard, bool eclate, double balancement)
        {
            if (dessins == null || dessin >= dessins.Length) return;
            if (!coeurs && dessin <= DessinCoeurRouge) return;          // cœurs désactivés dans le menu : seules les notes s'envolent
            Particule p = null;
            foreach (Particule libre in particules)
                if (libre.Libre) { p = libre; break; }
            if (p == null)
            {
                if (particules.Count >= 24) return;
                p = new Particule();
                particules.Add(p);
            }
            p.Libre = false;
            p.Image = dessins[dessin];
            p.Depart = depart;
            p.Controle = controle;
            p.Arrivee = arrivee;
            p.Duree = duree;
            p.Taille0 = taille0;
            p.Taille1 = taille1;
            p.Retard = retard;
            p.Eclate = eclate;
            p.Balancement = balancement;
            p.Debut = DateTime.UtcNow;
            // les bisous passent devant elle, vers l'écran ; cœurs et notes montent derrière sa bulle
            p.Fenetre.Dessous = anim.Ligne == LigneBisou ? IntPtr.Zero : poignee;
            minuteurParticules.Start();
        }

        void AnimerParticules(object s, EventArgs e)
        {
            DateTime maintenant = DateTime.UtcNow;
            bool encore = false;
            foreach (Particule p in particules)
            {
                if (p.Libre) continue;
                double t = ((maintenant - p.Debut).TotalSeconds - p.Retard) / p.Duree;
                if (t < 0) { encore = true; continue; }          // pas encore parti
                if (t >= (p.Eclate ? 1.2 : 1))
                {
                    p.Fenetre.Hide();
                    p.Libre = true;
                    continue;
                }
                encore = true;
                double k = Math.Min(1, t), u = 1 - k;
                double x = u * u * p.Depart.X + 2 * u * k * p.Controle.X + k * k * p.Arrivee.X + p.Balancement * Math.Sin(k * Math.PI * 3);
                double y = u * u * p.Depart.Y + 2 * u * k * p.Controle.Y + k * k * p.Arrivee.Y;
                double taille = p.Taille0 + (p.Taille1 - p.Taille0) * k;
                double opacite = Math.Min(1, k / 0.12);          // il apparaît
                if (t > 1)
                {
                    double eclat = (t - 1) / 0.2;                // arrivé au curseur : il éclate
                    taille *= 1 + 0.8 * eclat;
                    opacite = 1 - eclat;
                }
                else if (!p.Eclate && k > 0.55) opacite = Math.Min(opacite, (1 - k) / 0.45);   // il s'efface en montant
                p.Fenetre.Opacity = opacite;
                p.Fenetre.Montrer(p.Image, new Point(x - taille / 2, y - taille / 2), taille);
            }
            if (!encore) minuteurParticules.Stop();
        }

        // ------------------------------------------------------------------- Win32

        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
        const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        delegate bool RappelFenetre(IntPtr fenetre, IntPtr parametre);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(RappelFenetre rappel, IntPtr parametre);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr fenetre);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr fenetre);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr fenetre);

        [DllImport("user32.dll")]
        static extern int GetWindowTextLength(IntPtr fenetre);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr fenetre, out uint processus);

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr fenetre, uint drapeau);

        const int SW_RESTORE = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr fenetre, StringBuilder texte, int taille);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr fenetre, StringBuilder texte, int taille);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr fenetre, int commande);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr fenetre);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr fenetre, int attribut, out RECT valeur, int taille);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr fenetre, int attribut, out int valeur, int taille);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr fenetre, int index);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr fenetre, int index, int valeur);

        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr fenetre, IntPtr apres, int x, int y, int l, int h, uint drapeaux);
    }
}
