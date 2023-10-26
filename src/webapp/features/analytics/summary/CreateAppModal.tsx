import { useApps } from "@features/apps";
import { Dialog, Transition } from "@headlessui/react";
import { Fragment, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@components/Button";
import { IconX } from "@tabler/icons-react";
import { TextInput } from "@components/TextInput";

type Props = {
  open: boolean;
  onClose: () => void;
};

export function CreateAppModal(props: Props) {
  const { createApp } = useApps();
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [processing, setIsProcessing] = useState(false);

  const close = () => {
    setName("");
    setIsProcessing(false);
    props.onClose();
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsProcessing(true);

    const app = await createApp(name);
    navigate(app.id);

    return close();
  };

  return (
    <Transition.Root show={props.open} as={Fragment}>
      <Dialog as="div" className="relative z-50" onClose={close}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black bg-opacity-10 backdrop-blur-sm transition-opacity" />
        </Transition.Child>

        <div className="fixed inset-0 z-10 overflow-y-auto">
          <div className="flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 translate-y-4 sm:translate-y-0 sm:scale-95"
              enterTo="opacity-100 translate-y-0 sm:scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 translate-y-0 sm:scale-100"
              leaveTo="opacity-0 translate-y-4 sm:translate-y-0 sm:scale-95"
            >
              <Dialog.Panel className="relative transform overflow-hidden rounded-lg bg-background border px-4 pt-5 pb-4 text-left shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-md sm:p-6">
                <div className="absolute top-0 right-0 hidden pt-4 pr-4 sm:block">
                  <button
                    type="button"
                    className="rounded-md text-muted-foreground hover:text-foreground"
                    onClick={props.onClose}
                  >
                    <IconX className="h-6 w-6" />
                  </button>
                </div>
                <Dialog.Title as="h3" className="text-lg font-medium">
                  Register a new app
                </Dialog.Title>
                <form onSubmit={handleSubmit} className="mt-2">
                  <TextInput
                    name="name"
                    placeholder="App Name"
                    autoComplete="off"
                    required={true}
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                  />
                  <p className="text-sm text-muted-foreground mt-1">
                    A friendly name to identify your app. You can change it later.
                  </p>
                  <Button
                    className="mt-4"
                    disabled={name.length < 2 || name.length > 40 || processing}
                    loading={processing}
                  >
                    Confirm
                  </Button>
                </form>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition.Root>
  );
}
