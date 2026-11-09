using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarSales
{
    public class PauseMenuUI : UIPanel
    {
        [SerializeField] string mainMenuScene = "MainMenu";
        Button mainMenuButton;

        public override void Build()
        {
            var body = UIFactory.Window(Root, Loc.T("pause.title", "Paused"), new Vector2(520f, 680f), CloseAll);
            UIFactory.Vertical(body, 0, 14f);
            UIFactory.Btn(body, Loc.T("pause.resume", "Resume"), UIFactory.Good, CloseAll, 62f, 22f);
            UIFactory.Btn(body, Loc.T("pause.save", "Save Game"), UIFactory.Accent, () => { CarSalesServices.Save.SaveGame(); CarSalesServices.UI.Success("Saved", "Your progress has been saved."); }, 62f, 22f);
            UIFactory.Btn(body, Loc.T("menu.settings", "Settings"), UIFactory.Neutral, () => CarSalesServices.UI.settings.Show(), 62f, 22f);
            UIFactory.Btn(body, Loc.T("menu.credits", "Credits"), UIFactory.Neutral, () => CreditsUI.Show(transform.root), 52f, 20f);
            mainMenuButton = UIFactory.Btn(body, Loc.T("pause.mainmenu", "Save & Main Menu"), UIFactory.AccentDark, GoToMainMenu, 62f, 22f);
            UIFactory.Btn(body, Loc.T("pause.quit", "Save & Quit"), UIFactory.Bad, () => { CarSalesServices.Save.SaveGame(); QuitGame(); }, 62f, 22f);
        }

        public void Show()
        {
            OpenSelf();
            mainMenuButton.interactable = Application.CanStreamedLevelBeLoaded(mainMenuScene);
        }

        void GoToMainMenu()
        {
            if (!(CarSalesServices.Progress && CarSalesServices.Progress.IsGameOver)) CarSalesServices.Save.SaveGame();
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuScene);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
