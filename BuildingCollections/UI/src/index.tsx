import { ModRegistrar } from "cs2/modding";
import { GameTopLeftButton } from "./panel";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append("GameTopLeft", GameTopLeftButton);
};

export default register;
