﻿using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace QuestPDF.Elements
{
    internal class InlinedElement : Container
    {

    }

    internal enum InlinedAlignment
    {
        Left,
        Center,
        Right,
        Justify,
        SpaceAround
    }
    
    internal class Inlined : Element, IStateResettable
    {
        public List<InlinedElement> Elements { get; internal set; } = new List<InlinedElement>();
        private Queue<InlinedElement> ChildrenQueue { get; set; }

        internal float VerticalSpacing { get; set; }
        internal float HorizontalSpacing { get; set; }
        
        internal InlinedAlignment ElementsAlignment { get; set; }
        internal VerticalAlignment BaselineAlignment { get; set; }
        
        public void ResetState()
        {
            ChildrenQueue = new Queue<InlinedElement>(Elements);
        }
        
        internal override IEnumerable<Element?> GetChildren()
        {
            return Elements;
        }
        
        internal override SpacePlan Measure(Size availableSpace)
        {
            if (!ChildrenQueue.Any())
                return SpacePlan.FullRender(Size.Zero);
            
            var lines = Compose(availableSpace);

            if (!lines.Any())
                return SpacePlan.Wrap();

            var lineSizes = lines
                .Select(line =>
                {
                    var size = GetLineSize(line);

                    var widthWithSpacing = size.Width + (line.Count - 1) * HorizontalSpacing;
                    return new Size(widthWithSpacing, size.Height);
                })
                .ToList();
            
            var width = lineSizes.Max(x => x.Width);
            var height = lineSizes.Sum(x => x.Height) + (lines.Count - 1) * VerticalSpacing;
            var targetSize = new Size(width, height);

            var isPartiallyRendered = lines.Sum(x => x.Count) != ChildrenQueue.Count;

            if (isPartiallyRendered)
                return SpacePlan.PartialRender(targetSize);
            
            return SpacePlan.FullRender(targetSize);
        }

        internal override void Draw(Size availableSpace)
        {
            var lines = Compose(availableSpace);
            var topOffset = 0f;
            
            foreach (var line in lines)
            {
                var height = line
                    .Select(x => x.Measure(Size.Max))
                    .Where(x => x.Type != SpacePlanType.Wrap)
                    .Max(x => x.Height);
                
                DrawLine(line);

                topOffset += height + VerticalSpacing;
                Canvas.Translate(new Position(0, height + VerticalSpacing));
            }
            
            Canvas.Translate(new Position(0, -topOffset));
            lines.SelectMany(x => x).ToList().ForEach(x => ChildrenQueue.Dequeue());

            void DrawLine(ICollection<InlinedElement> elements)
            {
                var lineSize = GetLineSize(elements);

                var elementOffset = ElementOffset();
                var leftOffset = AlignOffset();
                Canvas.Translate(new Position(leftOffset, 0));
                
                foreach (var element in elements)
                {
                    var size = (Size)element.Measure(Size.Max);
                    var baselineOffset = BaselineOffset(size, lineSize.Height);

                    if (size.Height == 0)
                        size = new Size(size.Width, lineSize.Height);
                    
                    Canvas.Translate(new Position(0, baselineOffset));
                    element.Draw(size);
                    Canvas.Translate(new Position(0, -baselineOffset));

                    leftOffset += size.Width + elementOffset;
                    Canvas.Translate(new Position(size.Width + elementOffset, 0));
                }
                
                Canvas.Translate(new Position(-leftOffset, 0));

                float ElementOffset()
                {
                    var difference = availableSpace.Width - lineSize.Width;

                    if (elements.Count == 1)
                        return 0;

                    return ElementsAlignment switch
                    {
                        InlinedAlignment.Justify => difference / (elements.Count - 1),
                        InlinedAlignment.SpaceAround => difference / (elements.Count + 1),
                        _ => HorizontalSpacing
                    };
                }

                float AlignOffset()
                {
                    var difference = availableSpace.Width - lineSize.Width - (elements.Count - 1) * HorizontalSpacing;

                    return ElementsAlignment switch
                    {
                        InlinedAlignment.Left => 0,
                        InlinedAlignment.Justify => 0,
                        InlinedAlignment.SpaceAround => elementOffset,
                        InlinedAlignment.Center => difference / 2,
                        InlinedAlignment.Right => difference,
                        _ => 0
                    };
                }
                
                float BaselineOffset(Size elementSize, float lineHeight)
                {
                    var difference = lineHeight - elementSize.Height;

                    return BaselineAlignment switch
                    {
                        VerticalAlignment.Top => 0,
                        VerticalAlignment.Middle => difference / 2,
                        _ => difference
                    };
                }
            }
        }

        Size GetLineSize(ICollection<InlinedElement> elements)
        {
            var sizes = elements
                .Select(x => x.Measure(Size.Max))
                .Where(x => x.Type != SpacePlanType.Wrap)
                .ToList();
            
            var width = sizes.Sum(x => x.Width);
            var height = sizes.Max(x => x.Height);

            return new Size(width, height);
        }
        
        // list of lines, each line is a list of elements
        private ICollection<ICollection<InlinedElement>> Compose(Size availableSize)
        {
            var queue = new Queue<InlinedElement>(ChildrenQueue);
            var result = new List<ICollection<InlinedElement>>();

            var topOffset = 0f;
            
            while (true)
            {
                var line = GetNextLine();
                
                if (!line.Any())
                    break;

                var height = line
                    .Select(x => x.Measure(availableSize))
                    .Where(x => x.Type != SpacePlanType.Wrap)
                    .Max(x => x.Height);
                
                if (topOffset + height > availableSize.Height + Size.Epsilon)
                    break;

                topOffset += height + VerticalSpacing;
                result.Add(line);
            }

            return result;

            ICollection<InlinedElement> GetNextLine()
            {
                var result = new List<InlinedElement>();
                var leftOffset = GetInitialAlignmentOffset();
                
                while (true)
                {
                    if (!queue.Any())
                        break;
                    
                    var element = queue.Peek();
                    var size = element.Measure(Size.Max);
                    
                    if (size.Type == SpacePlanType.Wrap)
                        break;
                    
                    if (leftOffset + size.Width > availableSize.Width + Size.Epsilon)
                        break;

                    queue.Dequeue();
                    leftOffset += size.Width + HorizontalSpacing;
                    result.Add(element);    
                }

                return result;
            }

            float GetInitialAlignmentOffset()
            {
                // this method makes sure that the spacing between elements is no lesser than configured

                return ElementsAlignment switch
                {
                    InlinedAlignment.SpaceAround => HorizontalSpacing * 2,
                    _ => 0
                };
            }
        }
    }
}