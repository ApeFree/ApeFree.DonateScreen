using System;
using System.Collections.Generic;
using System.Drawing;

namespace ApeFree.DonateScreen.Codecs
{
    /// <summary>
    /// 脏矩形优化与合并器。
    /// 负责将 DXGI 返回的大量细碎散布的脏矩形按邻近度聚类合并，降低 JPEG 编码头开销。
    /// </summary>
    public static class DirtyRectOptimizer
    {
        /// <summary>
        /// 合并相邻距离小于指定阈值的脏矩形
        /// </summary>
        /// <param name="rects">输入的脏矩形集合</param>
        /// <param name="mergeDistance">允许合并的最大间隙距离（像素）</param>
        /// <param name="maxCount">允许的最大块数，超出时强制做整体包围合并</param>
        /// <returns>合并后的矩形集合</returns>
        public static List<Rectangle> Optimize(IEnumerable<Rectangle> rects, int mergeDistance = 16, int maxCount = 8)
        {
            var list = new List<Rectangle>();
            foreach (var r in rects)
            {
                if (r.Width > 0 && r.Height > 0)
                {
                    list.Add(r);
                }
            }

            if (list.Count <= 1)
            {
                return list;
            }

            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var r1 = list[i];
                        var r2 = list[j];

                        // 计算是否相交或间距小于 mergeDistance
                        var expanded1 = Rectangle.Inflate(r1, mergeDistance, mergeDistance);
                        if (expanded1.IntersectsWith(r2))
                        {
                            var union = Rectangle.Union(r1, r2);
                            list[i] = union;
                            list.RemoveAt(j);
                            merged = true;
                            break;
                        }
                    }
                    if (merged) break;
                }
            } while (merged && list.Count > 1);

            // 如果合并后仍然过多（例如超大面积动画或滚动），为保证实时吞吐，直接聚合为一个外接包围盒
            if (list.Count > maxCount)
            {
                var totalUnion = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    totalUnion = Rectangle.Union(totalUnion, list[i]);
                }
                list.Clear();
                list.Add(totalUnion);
            }

            return list;
        }

        /// <summary>
        /// 限制矩形在显示器可视范围内
        /// </summary>
        public static Rectangle Clamp(Rectangle rect, int maxWidth, int maxHeight)
        {
            int x = Math.Max(0, rect.X);
            int y = Math.Max(0, rect.Y);
            int w = Math.Min(rect.Width, maxWidth - x);
            int h = Math.Min(rect.Height, maxHeight - y);
            if (w < 0) w = 0;
            if (h < 0) h = 0;
            return new Rectangle(x, y, w, h);
        }
    }
}
