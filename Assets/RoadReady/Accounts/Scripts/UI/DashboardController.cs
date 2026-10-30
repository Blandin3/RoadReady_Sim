using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadReady.Accounts
{
    /// <summary>
    /// Dashboard: the teacher types the school password again, then sees how many members registered (total, today,
    /// last 7 days) and the newest members.
    /// </summary>
    public class DashboardController : MonoBehaviour
    {
        const int k_Shown = 6;

        RectTransform m_Content, m_Locked, m_Unlocked;
        TMP_InputField m_Password;
        TMP_Text m_Message;
        Button m_OpenButton;

        void Start()
        {
            m_Content = UiKit.CreateScreen("Dashboard");
            m_Message = UiKit.Label(m_Content, "Type the school password to open the dashboard.", 28f, UiKit.Muted, FontStyles.Normal, 60f);

            m_Locked = UiKit.Group(m_Content);
            m_Password = UiKit.MakeInput(m_Locked, "School password", password: true);
            m_OpenButton = UiKit.MakeButton(m_Locked, "Open dashboard", Unlock, height: 72f);

            m_Unlocked = UiKit.Group(m_Content);
            m_Unlocked.gameObject.SetActive(false);

            UiKit.MakeButton(m_Content, "Back to Home", () => SceneRouter.Go(SceneRouter.Home), primary: false, height: 64f);
            UiKit.Keyboard(m_Content);
            UiKit.Focus(m_Password);
        }

        async void Unlock()
        {
            m_OpenButton.interactable = false;
            UiKit.ShowMessage(m_Message, "Checking...", isError: false);
            try
            {
                await AuthService.EnsureReadyAsync();
                if (!AuthService.IsLoggedIn)
                {
                    SceneRouter.Go(SceneRouter.SchoolSetup);
                    return;
                }

                if (!await AuthService.ReauthenticateAsync(m_Password.text))
                {
                    UiKit.ShowMessage(m_Message, "That password is not right.");
                    m_OpenButton.interactable = true;
                    return;
                }

                var members = await MemberService.ListAsync(AuthService.SchoolId);
                m_Password.text = "";
                m_Locked.gameObject.SetActive(false);
                Fill(members);
            }
            catch (Exception e)
            {
                UiKit.ShowMessage(m_Message, e.Message);
                m_OpenButton.interactable = true;
            }
        }

        void Fill(List<MemberRecord> members)
        {
            var now = DateTime.Now;
            UiKit.ShowMessage(m_Message, "", isError: false);
            UiKit.Label(m_Unlocked, $"Members registered: {members.Count}", 34f, UiKit.Accent, FontStyles.Bold, 50f);
            UiKit.Label(m_Unlocked, $"Today: {MemberService.CountToday(members, now)}     Last 7 days: {MemberService.CountLast7Days(members, now)}", 28f, UiKit.TextColor, FontStyles.Normal, 44f);
            UiKit.Label(m_Unlocked, members.Count == 0 ? "Nobody has registered yet." : "Newest members", 26f, UiKit.Muted, FontStyles.Normal, 40f);
            for (var i = 0; i < Mathf.Min(members.Count, k_Shown); i++)
                UiKit.Label(m_Unlocked, $"{members[i].Label}   {members[i].CreatedAt.ToLocalTime():d MMM}", 28f, UiKit.TextColor, FontStyles.Normal, 44f);
            m_Unlocked.gameObject.SetActive(true);
        }
    }
}
