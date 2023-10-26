import { Application } from "@features/apps";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@components/Tooltip";
import { api } from "@fns/api";
import { IconShareOff } from "@tabler/icons-react";
import { Button } from "@components/Button";
import { formatDate } from "@fns/format-date";

type Props = {
  app: Application;
  email: string;
  createdAt: string;
  onUnshare: () => void;
};

export function AppShareListItem(props: Props) {
  const unshare = async (e: React.SyntheticEvent<HTMLButtonElement>) => {
    e.preventDefault();

    await api.delete(`/_apps/${props.app.id}/shares/${props.email.trim()}`);
    props.onUnshare();
  };

  return (
    <li className="flex items-center bg-card space-x-4 border px-4 py-2 rounded-md justify-between">
      <div className="truncate">
        <p className="truncate">{props.email}</p>
        <p className="text-xs text-muted-foreground mt-1">{formatDate(props.createdAt)}</p>
      </div>
      <TooltipProvider>
        <Tooltip delayDuration={0}>
          <TooltipTrigger asChild>
            <Button variant="ghost" size="xs" onClick={unshare}>
              <IconShareOff className="w-4 h-4" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>Unshare</TooltipContent>
        </Tooltip>
      </TooltipProvider>
    </li>
  );
}
