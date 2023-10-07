import { useState, type MouseEvent } from "react";
import { WorldMapPopup } from "./WorldMapPopup";
import { projectAbsolute } from "./robinson";
import { CountryFlag, CountryName } from "@features/geo";

type Props = {
  region: string;
  countryCode: string;
  lat: number;
  lng: number;
  size: number;
};

type TooltipPosition = {
  x: number;
  y: number;
};

export function MapDataPoint(props: Props) {
  const [tooltip, setTooltip] = useState<TooltipPosition | undefined>(undefined);

  const mouseOver = (evt: MouseEvent) => {
    if (tooltip) return;

    setTooltip({ x: evt.clientX, y: evt.clientY });
  };

  const mouseOut = () => setTooltip(undefined);

  const { x, y } = projectAbsolute(props.lat, props.lng, 2000, 1, -30, 0);
  return (
    <>
      <circle
        cx={x}
        cy={y}
        r={props.size}
        onMouseEnter={mouseOver}
        onMouseLeave={mouseOut}
        className="text-success opacity-50"
      />

      {tooltip && (
        <WorldMapPopup {...tooltip}>
          <div className="p-2 text-sm">
            <div className="flex items-center justify-between">
              <span>
                <span className="font-bold">5</span> recent users
              </span>
              <CountryFlag countryCode={props.countryCode} size="sm" />
            </div>

            <div>
              {props.region}, <CountryName countryCode={props.countryCode} />
            </div>
          </div>
        </WorldMapPopup>
      )}
    </>
  );
}