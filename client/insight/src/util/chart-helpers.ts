import { MouseEvent, TouchEvent } from "react";

export function localPoint(evt: MouseEvent | TouchEvent): DOMPoint | null {
  const { target } = evt;
  if (!(target instanceof SVGElement)) return null;

  const svg = target instanceof SVGSVGElement ? target : target.ownerSVGElement;
  if (!svg) return null;

  const screenCTM = svg.getScreenCTM();
  if (!screenCTM) return null;

  const point = svg.createSVGPoint();
  if ("changedTouches" in evt) {
    point.x = evt.changedTouches[0].clientX;
    point.y = evt.changedTouches[0].clientY;
  } else {
    point.x = evt.clientX;
    point.y = evt.clientY;
  }

  return point.matrixTransform(screenCTM.inverse());
}

const measureTextId = "__bms_measure_text_element";

export function measureSvgString(str: string, fontSize?: number) {
  try {
    const existingElem = document.getElementById(measureTextId);
    let txtElem: SVGTextElement;
    if (existingElem instanceof SVGTextElement) {
      txtElem = existingElem;
    } else {
      const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
      svg.setAttribute("aria-hidden", "true");
      svg.style.position = "absolute";
      svg.style.top = "-100%";
      svg.style.left = "-100%";
      svg.style.width = "0";
      svg.style.height = "0";
      svg.style.opacity = "0";
      const textElem = document.createElementNS("http://www.w3.org/2000/svg", "text");
      if (!(textElem instanceof SVGTextElement)) {
        return null;
      }
      txtElem = textElem;
      txtElem.setAttribute("id", measureTextId);
      if (fontSize) {
        txtElem.style.fontSize = fontSize.toString();
      }
      svg.appendChild(txtElem);
      document.body.appendChild(svg);
    }

    txtElem.textContent = str;
    return txtElem.getComputedTextLength();
  } catch {
    return null;
  }
}
