// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

using Size = System.Windows.Size;

namespace PDownloader.Controls;

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
        {
            offsetY = 0;
        }

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
