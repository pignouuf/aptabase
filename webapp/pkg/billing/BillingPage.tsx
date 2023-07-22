import {
  ErrorState,
  Head,
  LoadingState,
  PageHeading,
  api,
} from "@app/primitives";
import { CurrentUsage } from "./CurrentUsage";
import { CurrentPlan } from "./CurrentPlan";
import { useBilling } from "./useBilling";

Component.displayName = "Billing";
export function Component() {
  const { isLoading, isError, data } = useBilling();

  return (
    <>
      <Head title="Billing" />
      <PageHeading title="Billing" subtitle="Manage your subscription" />

      <div className="flex flex-col gap-8 max-w-3xl mt-8">
        {isLoading && <LoadingState />}
        {isError && <ErrorState />}
        {data && (
          <div className="grid lg:grid-cols-2 items-stretch gap-8">
            <div className="border rounded-md p-4 min-h-[6rem]">
              <CurrentPlan billing={data} />
            </div>
            <div className="border rounded-md p-4 min-h-[6rem]">
              <CurrentUsage billing={data} />
            </div>
          </div>
        )}
      </div>
    </>
  );
}