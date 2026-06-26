using Size = System.Windows.Size;

namespace PDownloader.Controls
{
    public class CustomVirtualizingWrapPanel : VirtualizingWrapPanel
    {
        public bool EnableCustomBehavior { get; set; } = true;

        protected override Size MeasureOverride(Size availableSize)
        {
            return base.MeasureOverride(availableSize);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var offsetX = GetX(Offset);
            var offsetY = GetY(Offset);

            if (ItemsOwner is IHierarchicalVirtualizationAndScrollInfo groupItem)
                offsetY = 0;

            Size childSize = CalculateChildArrangeSize(finalSize);
            CalculateSpacing(finalSize, out double innerSpacing, out double outerSpacing);

            if (Orientation == System.Windows.Controls.Orientation.Horizontal)
            {
                childSize = new Size(finalSize.Width, childSize.Height);
            }

            for (int childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
            {
                UIElement child = InternalChildren[childIndex];
                int itemIndex = GetItemIndexFromChildIndex(childIndex);

                double x, y;
                int columnIndex = itemIndex % ItemsPerRowCount;
                int rowIndex = itemIndex / ItemsPerRowCount;

                x = outerSpacing + columnIndex * (GetWidth(childSize) + innerSpacing);
                y = rowIndex * GetHeight(childSize);

                if (GetHeight(finalSize) == 0.0)
                {
                    child.Arrange(new Rect(0, 0, 0, 0));
                }
                else
                {
                    child.Arrange(CreateRect(x - offsetX, y - offsetY, childSize.Width, childSize.Height));
                }
            }

            return finalSize;
        }


        protected override void OnOrientationChanged()
        {
            base.OnOrientationChanged();
        }
    }
}
