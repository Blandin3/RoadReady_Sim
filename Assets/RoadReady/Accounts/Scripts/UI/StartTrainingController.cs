using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoadReady.Accounts
{
    /// <summary>Start training: pick who is training from the school's members (or search for yourself), then open the simulator.</summary>
    public class StartTrainingController : MonoBehaviour
    {
        const int k_PageSize = 6;
        const int k_PageSizeWithKeyboard = 3; // the keyboard takes the room of the other rows

        RectTransform m_Content, m_List, m_KeyboardGroup;
        TMP_Text m_Message, m_PageLabel;
        Button m_Previous, m_Next, m_Back;
        TMP_InputField m_Search;
        List<MemberRecord> m_Members = new List<MemberRecord>();
        readonly List<MemberRecord> m_Shown = new List<MemberRecord>();
        int m_Page;
        bool m_KeyboardOpen;

        int PageSize => m_KeyboardOpen ? k_PageSizeWithKeyboard : k_PageSize;

        async void Start()
        {
            m_Content = UiKit.CreateScreen("Start training");
            m_Message = UiKit.Label(m_Content, "Loading members...", 28f, UiKit.Muted, FontStyles.Normal, 50f);

            // Search by name or code (R-004): the learner finds themselves without paging through the list.
            m_Search = UiKit.MakeSearch(m_Content, "Search your name or code");
            m_Search.onValueChanged.AddListener(_ => ShowPage(0));
            m_Search.onSelect.AddListener(_ => OpenKeyboard());
            UiKit.Focus(m_Search);

            m_List = UiKit.Group(m_Content);

            var paging = UiKit.Row(m_Content, 64f);
            m_Previous = UiKit.MakeButton(paging, "Previous", () => ShowPage(m_Page - 1), primary: false);
            m_PageLabel = UiKit.Label(paging, "", 26f, UiKit.Muted);
            m_PageLabel.textWrappingMode = TextWrappingModes.NoWrap;
            m_Next = UiKit.MakeButton(paging, "Next", () => ShowPage(m_Page + 1), primary: false);
            // Fixed-size buttons at each end and the page number in the middle, like the other buttons on the screen.
            foreach (var button in new[] { m_Previous, m_Next })
            {
                var element = button.GetComponent<LayoutElement>();
                element.preferredWidth = 240f;
                element.minWidth = 240f;
                element.flexibleWidth = 0f;
            }

            m_PageLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;

            // The keyboard opens when the search box is tapped and closes with Done.
            m_KeyboardGroup = UiKit.Group(m_Content);
            UiKit.Keyboard(m_KeyboardGroup);
            UiKit.MakeButton(m_KeyboardGroup, "Done", CloseKeyboard, height: 56f);
            m_KeyboardGroup.gameObject.SetActive(false);

            m_Back = UiKit.MakeButton(m_Content, "Back to Home", () => SceneRouter.Go(SceneRouter.Home), primary: false, height: 72f);
            ShowPage(0);

            try
            {
                await AuthService.EnsureReadyAsync();
                if (!AuthService.IsLoggedIn)
                {
                    SceneRouter.Go(SceneRouter.SchoolSetup);
                    return;
                }

                m_Members = await MemberService.ListAsync(AuthService.SchoolId);
                ShowPage(0);
            }
            catch (Exception e)
            {
                UiKit.ShowMessage(m_Message, e.Message);
            }
        }

        void OpenKeyboard()
        {
            if (m_KeyboardOpen)
                return;
            m_KeyboardOpen = true;
            m_KeyboardGroup.gameObject.SetActive(true);
            m_Back.gameObject.SetActive(false);
            ShowPage(0);
        }

        void CloseKeyboard()
        {
            m_KeyboardOpen = false;
            m_KeyboardGroup.gameObject.SetActive(false);
            m_Back.gameObject.SetActive(true);
            ShowPage(m_Page);
        }

        void ShowPage(int page)
        {
            if (m_Search == null)
                return;
            var query = m_Search.text.Trim();
            m_Shown.Clear();
            foreach (var member in m_Members)
                if (query.Length == 0 || Matches(member, query))
                    m_Shown.Add(member);

            var pageSize = PageSize;
            var pages = Mathf.Max(1, Mathf.CeilToInt(m_Shown.Count / (float)pageSize));
            m_Page = Mathf.Clamp(page, 0, pages - 1);

            foreach (Transform child in m_List)
                Destroy(child.gameObject);
            for (var i = m_Page * pageSize; i < Mathf.Min(m_Shown.Count, (m_Page + 1) * pageSize); i++)
            {
                var member = m_Shown[i];
                UiKit.MakeButton(m_List, member.Label, () => Train(member), height: 72f);
            }

            if (m_Members.Count == 0)
                UiKit.ShowMessage(m_Message, "No members yet. Go back and choose Register new member first.", isError: true);
            else if (m_Shown.Count == 0)
                UiKit.ShowMessage(m_Message, "No member matches that search.", isError: false);
            else
                UiKit.ShowMessage(m_Message, query.Length == 0 ? "Who is training?" : $"{m_Shown.Count} found - tap your name", isError: false);

            m_PageLabel.text = $"Page {m_Page + 1} of {pages}";
            m_Previous.interactable = m_Page > 0;
            m_Next.interactable = m_Page < pages - 1;
        }

        static bool Matches(MemberRecord member, string query) =>
            (member.FullName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (member.Code ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        void Train(MemberRecord member)
        {
            CurrentMember.Set(AuthService.SchoolId, member);
            RoadReady.Core.SchoolHandoff.Set(member.FullName, member.Code); // the game skips its own name page and returns here at the end
            if (!SceneRouter.Go(SceneRouter.Game))
                UiKit.ShowMessage(m_Message, "The simulator scene (RR_Bootstrap) is not in Build Settings.");
        }
    }
}
