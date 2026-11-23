using System;
using System.Collections.Generic;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Workspace
{
    // A page of buttons in a VirtualScreen's text area (2 x 3, the last place "Dalej" when there are
    // more): the "Więcej" menu and the lists of PDFs, environments, models and spatial photos use it.
    public class ChoiceList : MonoBehaviour
    {
        const int k_Columns = 2;
        const int k_Rows = 3;
        const float k_ButtonWidth = 222f;

        public struct Choice
        {
            public string Label;
            public string Icon;
            public Action Picked;
            public Choice(string label, Action picked, string icon = null) { Label = label; Picked = picked; Icon = icon; }
        }

        VirtualScreen m_Screen;
        RectTransform m_Group;
        TextMeshProUGUI m_Message;
        readonly List<Choice> m_Choices = new();
        int m_Page;
        bool m_CloseOnPick;

        // closeOnPick: the window closes after a choice (a menu); otherwise the owner replaces it
        public void Initialize(VirtualScreen screen, string title, IEnumerable<Choice> choices, string emptyMessage = null,
            bool closeOnPick = true)
        {
            m_Screen = screen;
            m_CloseOnPick = closeOnPick;
            m_Screen.SetTitle(title);
            m_Screen.ShowBody(false);
            m_Screen.ShowFooterStats(false);
            m_Group = M3.Group("ChoiceList", m_Screen.Rect, VirtualScreen.BodyCenter, VirtualScreen.BodySize);
            m_Message = M3.Text("ChoiceMessage", m_Screen.Rect, emptyMessage ?? "", M3Theme.Type.BodyMedium,
                VirtualScreen.BodyCenter, new Vector2(VirtualScreen.BodySize.x, 120f), M3Theme.OnSurfaceVariant);
            m_Choices.AddRange(choices);
            m_Message.gameObject.SetActive(m_Choices.Count == 0);
            ShowPage();
        }

        void ShowPage()
        {
            foreach (Transform child in m_Group)
                Destroy(child.gameObject);
            int perPage = m_Choices.Count > k_Columns * k_Rows ? k_Columns * k_Rows - 1 : k_Columns * k_Rows;
            int pages = Mathf.Max(1, Mathf.CeilToInt(m_Choices.Count / (float)perPage));
            m_Page %= pages;
            int first = m_Page * perPage;
            int shown = Mathf.Min(perPage, m_Choices.Count - first);
            for (int i = 0; i < shown + (pages > 1 ? 1 : 0); i++)
            {
                int column = i % k_Columns, row = i / k_Columns;
                var position = new Vector2((column - 0.5f) * (k_ButtonWidth + 8f), 64f - row * 64f);
                if (i < shown)
                {
                    var choice = m_Choices[first + i];
                    M3.Button($"Choice_{i}", m_Group, choice.Label, M3Button.Variant.Tonal, position, () => Pick(choice),
                        choice.Icon, k_ButtonWidth);
                }
                else
                {
                    M3.Button("ChoiceNext", m_Group, $"Dalej ({m_Page + 1}/{pages})", M3Button.Variant.Outlined, position,
                        () => { m_Page++; ShowPage(); }, null, k_ButtonWidth);
                }
            }
            WorkspaceSounds.Attach(m_Group);
        }

        void Pick(Choice choice)
        {
            if (m_CloseOnPick)
            {
                choice.Picked?.Invoke();
                m_Screen.Close();
                return;
            }
            Destroy(m_Group.gameObject);
            Destroy(m_Message.gameObject);
            Destroy(this);
            choice.Picked?.Invoke();
        }
    }
}
