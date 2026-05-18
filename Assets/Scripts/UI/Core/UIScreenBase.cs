using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BasketballManager.UI.Core
{
    public abstract class UIScreenBase : MonoBehaviour
    {
        protected static RectTransform CreateColumnPanel(RectTransform parent, float width)
        {
            var panel = CreatePanel("ColumnPanel", parent, new Color(0.11f, 0.12f, 0.16f));
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = panel.gameObject.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 1f;
            return panel;
        }

        protected static RectTransform CreateFlexiblePanel(RectTransform parent)
        {
            var panel = CreatePanel("FlexiblePanel", parent, new Color(0.11f, 0.12f, 0.16f));
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = panel.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.flexibleHeight = 1f;
            return panel;
        }

        protected static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var panelObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            var panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panelObject.GetComponent<Image>().color = color;
            return panel;
        }

        protected static Text CreateHeader(RectTransform parent, string text, int fontSize = 20)
        {
            var label = CreateBodyText(parent, text, fontSize);
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            LayoutElementWithHeight(label.gameObject, fontSize + 16f);
            return label;
        }

        protected static Text CreateBodyText(Transform parent, string text, int fontSize = 16)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var textComponent = textObject.GetComponent<Text>();
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.fontSize = fontSize;
            textComponent.color = new Color(0.9f, 0.92f, 0.96f);
            textComponent.alignment = TextAnchor.MiddleLeft;
            textComponent.text = text;
            return textComponent;
        }

        protected static RectTransform CreateScrollList(RectTransform parent)
        {
            var scroll = CreateScrollView(parent, out var content);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollLayout = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            return content;
        }

        protected static RectTransform CreateScrollView(RectTransform parent, out RectTransform content)
        {
            var scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            scrollObject.GetComponent<Image>().color = new Color(0.09f, 0.10f, 0.13f);

            var scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            viewportObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.03f);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);

            var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            Stretch(scrollRectTransform);

            var viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport);

            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return scrollRectTransform;
        }

        protected static Button CreateButton(RectTransform parent, string text, Action onClick)
        {
            var buttonObject = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.23f, 0.49f, 0.81f);

            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => onClick.Invoke());

            var layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 44f;

            var label = CreateBodyText(buttonObject.transform, text);
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            Stretch(label.rectTransform);
            return button;
        }

        protected static void CreateListButton(RectTransform parent, string text, Action onClick)
        {
            var button = CreateButton(parent, text, onClick);
            var image = button.GetComponent<Image>();
            image.color = new Color(0.18f, 0.20f, 0.26f);

            var layout = button.gameObject.GetComponent<LayoutElement>();
            layout.minHeight = 52f;
            layout.flexibleWidth = 1f;
        }

        protected static InputField CreateInputField(RectTransform parent)
        {
            var inputObject = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);

            inputObject.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f);
            var layout = inputObject.AddComponent<LayoutElement>();
            layout.minHeight = 38f;
            layout.flexibleWidth = 1f;

            var input = inputObject.GetComponent<InputField>();
            var text = CreateBodyText(inputObject.transform, string.Empty);
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            Stretch(text.rectTransform, 10f, 10f, 6f, 6f);

            var placeholder = CreateBodyText(inputObject.transform, "\u8bf7\u8f93\u5165");
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            Stretch(placeholder.rectTransform, 10f, 10f, 6f, 6f);

            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        protected static Dropdown CreateDropdown(RectTransform parent, IReadOnlyList<string> options)
        {
            var dropdownObject = new GameObject("Dropdown", typeof(RectTransform), typeof(Image), typeof(Dropdown));
            dropdownObject.transform.SetParent(parent, false);
            dropdownObject.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f);

            var layout = dropdownObject.AddComponent<LayoutElement>();
            layout.minHeight = 38f;
            layout.flexibleWidth = 1f;

            var captionText = CreateBodyText(dropdownObject.transform, string.Empty);
            captionText.alignment = TextAnchor.MiddleLeft;
            Stretch(captionText.rectTransform, 10f, 30f, 6f, 6f);

            var arrowText = CreateBodyText(dropdownObject.transform, "v");
            arrowText.alignment = TextAnchor.MiddleCenter;
            arrowText.color = Color.white;
            var arrowRect = arrowText.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = new Vector2(1f, 1f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.sizeDelta = new Vector2(24f, 0f);
            arrowRect.anchoredPosition = new Vector2(-8f, 0f);

            var dropdown = dropdownObject.GetComponent<Dropdown>();
            dropdown.targetGraphic = dropdownObject.GetComponent<Image>();
            dropdown.captionText = captionText;
            dropdown.options = new List<Dropdown.OptionData>();
            foreach (var option in options)
            {
                dropdown.options.Add(new Dropdown.OptionData(option));
            }

            var template = CreateDropdownTemplate(dropdownObject.transform, options);
            dropdown.template = template;
            dropdown.itemText = template.GetComponentInChildren<Text>();

            return dropdown;
        }

        protected static RectTransform CreateDropdownTemplate(Transform parent, IReadOnlyList<string> options)
        {
            var templateObject = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            templateObject.transform.SetParent(parent, false);
            templateObject.SetActive(false);
            templateObject.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.21f);

            var templateRect = templateObject.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -4f);
            templateRect.sizeDelta = new Vector2(0f, Mathf.Max(120f, options.Count * 30f + 12f));

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(templateObject.transform, false);
            viewportObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;
            Stretch(viewportObject.GetComponent<RectTransform>());

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(ToggleGroup));
            contentObject.transform.SetParent(viewportObject.transform, false);
            var contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;

            var contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 2f;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = templateObject.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.viewport = viewportObject.GetComponent<RectTransform>();
            scrollRect.content = contentRect;

            Toggle firstToggle = null;
            foreach (var option in options)
            {
                var itemObject = new GameObject(option, typeof(RectTransform), typeof(Image), typeof(Toggle));
                itemObject.transform.SetParent(contentObject.transform, false);
                itemObject.GetComponent<Image>().color = new Color(0.18f, 0.20f, 0.26f);

                var itemLayout = itemObject.AddComponent<LayoutElement>();
                itemLayout.minHeight = 28f;

                var itemText = CreateBodyText(itemObject.transform, option);
                itemText.alignment = TextAnchor.MiddleLeft;
                Stretch(itemText.rectTransform, 10f, 10f, 4f, 4f);

                var toggle = itemObject.GetComponent<Toggle>();
                toggle.targetGraphic = itemObject.GetComponent<Image>();
                toggle.graphic = null;
                if (firstToggle == null)
                {
                    firstToggle = toggle;
                }
            }

            var dropdown = parent.GetComponent<Dropdown>();
            if (dropdown != null && firstToggle != null)
            {
                dropdown.itemText = firstToggle.GetComponentInChildren<Text>();
            }

            return templateRect;
        }

        protected static void LayoutElementWithWidth(GameObject gameObject, float width)
        {
            var layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
        }

        protected static void LayoutElementWithHeight(GameObject gameObject, float height)
        {
            var layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
        }

        protected static void Stretch(RectTransform rectTransform, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(left, bottom);
            rectTransform.offsetMax = new Vector2(-right, -top);
        }

        protected static void ClearChildren(RectTransform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        protected static string FormatPercent(int made, int attempted)
        {
            if (attempted == 0) return "0.0%";
            return $"{(float)made / attempted * 100f:F1}%";
        }

        protected static Text CreateSectionTitle(RectTransform parent, string text)
        {
            var label = CreateHeader(parent, text, 22);
            label.color = new Color(0.85f, 0.85f, 0.90f);
            return label;
        }

        protected static Button CreateSmallButton(RectTransform parent, string text, Action onClick)
        {
            var button = CreateButton(parent, text, onClick);
            var layout = button.gameObject.GetComponent<LayoutElement>();
            layout.minHeight = 36f; // Smaller height
            var labelText = button.gameObject.GetComponentInChildren<Text>();
            if (labelText != null) labelText.fontSize = 16;
            return button;
        }

        protected static RectTransform CreateStatRow(RectTransform parent, string label, string homeVal, string awayVal)
        {
            var row = CreatePanel("Row", parent, Color.clear);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            LayoutElementWithHeight(row.gameObject, 28f);

            var homeText = CreateBodyText(row, homeVal);
            homeText.alignment = TextAnchor.MiddleCenter;
            homeText.fontSize = 18;

            var labelText = CreateBodyText(row, label);
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = new Color(0.6f, 0.6f, 0.6f);
            labelText.fontSize = 18;

            var awayText = CreateBodyText(row, awayVal);
            awayText.alignment = TextAnchor.MiddleCenter;
            awayText.fontSize = 18;

            return row;
        }

        protected static RectTransform CreateTableHeaderRow(RectTransform parent, IReadOnlyList<(string title, float width)> columns)
        {
            var headerRow = CreatePanel("Header", parent, new Color(0.15f, 0.16f, 0.20f));
            var hLayout = headerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(8, 8, 4, 4);
            hLayout.spacing = 8f;
            hLayout.childForceExpandHeight = false;
            hLayout.childForceExpandWidth = false;
            hLayout.childControlHeight = true;
            hLayout.childControlWidth = true;
            LayoutElementWithHeight(headerRow.gameObject, 32f);

            foreach (var col in columns)
            {
                var text = CreateBodyText(headerRow, col.title);
                text.color = new Color(0.7f, 0.7f, 0.75f);
                text.fontSize = 16;
                LayoutElementWithWidth(text.gameObject, col.width);
            }

            return headerRow;
        }

        protected static RectTransform CreateTableDataRow(RectTransform parent, IReadOnlyList<(string value, float width)> columns)
        {
            var row = CreatePanel("DataRow", parent, Color.clear);
            var rLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rLayout.padding = new RectOffset(8, 8, 4, 4);
            rLayout.spacing = 8f;
            rLayout.childForceExpandHeight = false;
            rLayout.childForceExpandWidth = false;
            rLayout.childControlHeight = true;
            rLayout.childControlWidth = true;
            LayoutElementWithHeight(row.gameObject, 32f);

            foreach (var col in columns)
            {
                var text = CreateBodyText(row, col.value);
                text.fontSize = 16;
                LayoutElementWithWidth(text.gameObject, col.width);
            }

            return row;
        }
    }
}
